using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using StoneFlow.Shared.Application.Scheduling;
using StoneFlow.Shared.Application.ShopOrders;

namespace StoneFlow.Integrations.Application.WooCommerce;

/// <summary>Wynik odczytu treści webhooka.</summary>
/// <param name="Snapshot">Zamówienie z dostawy albo <c>null</c>, gdy treść jest nieczytelna.</param>
/// <param name="Problem">Powód nieczytelności (bez danych klienta) do logu.</param>
/// <param name="Currency">Waluta z treści (diagnostyka; sklep sprzedaje tylko w PLN).</param>
/// <param name="ExternalId">ID zamówienia, gdy dało się je odczytać — także dla treści nieczytelnej
/// (klucz historii błędów, SF-13).</param>
/// <param name="ModifiedAtUtc">Data modyfikacji z treści, gdy czytelna — także dla treści nieczytelnej.</param>
public sealed record PayloadReadResult(
    ShopOrderSnapshot? Snapshot,
    string? Problem,
    string? Currency = null,
    string? ExternalId = null,
    DateTime? ModifiedAtUtc = null);

/// <summary>
/// Mapowanie zamówienia WooCommerce REST v3 (treść webhooka) na <see cref="ShopOrderSnapshot"/>
/// (SF-12, „Mapowanie payloadu”). Kwoty jako <c>decimal</c> z kropką, najwyżej 2 miejsca.
/// </summary>
public static partial class WooCommerceOrderPayload
{
    public const string TaxIdMetaKey = "_billing_billing_nip";

    /// <summary>Największa kwota przyjmowana przez ERP (<c>decimal(18,2)</c>).</summary>
    public const decimal MaxAmount = 9_999_999_999_999_999.99m;

    public static PayloadReadResult Read(ShopOrderTopic topic, ReadOnlySpan<byte> body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body.ToArray());
        }
        catch (JsonException)
        {
            return Unreadable("niepoprawny JSON");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || ReadId(root) is not { } id)
            {
                return Unreadable("brak id zamówienia");
            }

            if (topic == ShopOrderTopic.Deleted)
            {
                return new PayloadReadResult(ShopOrderSnapshot.Deletion(id), null, ExternalId: id);
            }

            var status = Text(root, "status");
            var created = ReadUtc(root, "date_created_gmt");
            var modified = ReadUtc(root, "date_modified_gmt");
            var total = ReadAmount(root, "total");
            if (status is null || created is null || modified is null || total is null)
            {
                return Unreadable($"brak wymaganych pól zamówienia {id}", id, modified);
            }

            if (total is < 0 or > MaxAmount)
            {
                return Unreadable($"wartość zamówienia {id} poza zakresem", id, modified);
            }

            if (ReadRefunds(root) is not { } refunded || refunded > MaxAmount)
            {
                return Unreadable($"nieczytelne zwroty zamówienia {id}", id, modified);
            }

            var orderDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(created.Value, WarsawTimeZone.Zone));
            var snapshot = new ShopOrderSnapshot(id, topic, modified, orderDate, status, total, refunded, ReadCustomer(root));
            return new PayloadReadResult(snapshot, null, Text(root, "currency"), id, modified);
        }
    }

    /// <summary>NIP bez spacji, myślników i prefiksu <c>PL</c>.</summary>
    public static string? NormalizeTaxId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var compact = TaxIdSeparators().Replace(text, string.Empty);
        if (compact.StartsWith("PL", StringComparison.OrdinalIgnoreCase))
        {
            compact = compact[2..];
        }

        return compact.Length == 0 ? null : compact;
    }

    private static PayloadReadResult Unreadable(string problem, string? externalId = null, DateTime? modifiedAtUtc = null) =>
        new(null, problem, ExternalId: externalId, ModifiedAtUtc: modifiedAtUtc);

    private static string? ReadId(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var id))
        {
            return null;
        }

        if (id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var number) && number > 0)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        return id.ValueKind == JsonValueKind.String && long.TryParse(id.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is { } text
        && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    private static DateTime? ReadUtc(JsonElement root, string name) =>
        Text(root, name) is { } text
        && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value)
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : null;

    private static decimal? ReadAmount(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
        if (text is null || !decimal.TryParse(text.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            return null;
        }

        return amount.Scale <= 2 || decimal.Round(amount, 2) == amount ? decimal.Round(amount, 2) : null;
    }

    /// <summary>Suma wartości bezwzględnych <c>refunds[].total</c>; brak tablicy → 0; nieczytelna pozycja → <c>null</c>.</summary>
    private static decimal? ReadRefunds(JsonElement root)
    {
        if (!root.TryGetProperty("refunds", out var refunds) || refunds.ValueKind == JsonValueKind.Null)
        {
            return 0;
        }

        if (refunds.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var sum = 0m;
        foreach (var refund in refunds.EnumerateArray())
        {
            if (ReadAmount(refund, "total") is not { } amount)
            {
                return null;
            }

            sum += Math.Abs(amount);
        }

        return sum;
    }

    private static ShopCustomer? ReadCustomer(JsonElement root)
    {
        if (!root.TryGetProperty("billing", out var billing) || billing.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var company = Text(billing, "company");
        var person = string.Join(' ', new[] { Text(billing, "first_name"), Text(billing, "last_name") }.OfType<string>());
        var street = string.Join(' ', new[] { Text(billing, "address_1"), Text(billing, "address_2") }.OfType<string>());
        return new ShopCustomer(
            company is null ? ShopCustomerKind.Person : ShopCustomerKind.Company,
            company ?? person,
            Text(billing, "phone"),
            Text(billing, "email"),
            street.Length == 0 ? null : street,
            Text(billing, "postcode"),
            Text(billing, "city"),
            NormalizeTaxId(ReadMeta(root, TaxIdMetaKey)));
    }

    private static string? ReadMeta(JsonElement root, string key)
    {
        if (!root.TryGetProperty("meta_data", out var meta) || meta.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in meta.EnumerateArray())
        {
            if (Text(item, "key") == key && item.TryGetProperty("value", out var value))
            {
                return value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.GetRawText(),
                    _ => null,
                };
            }
        }

        return null;
    }

    [GeneratedRegex(@"[\s\-]")]
    private static partial Regex TaxIdSeparators();
}
