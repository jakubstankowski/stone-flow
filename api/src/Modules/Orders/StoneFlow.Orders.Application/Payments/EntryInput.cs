using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using StoneFlow.Orders.Application.Abstractions;

namespace StoneFlow.Orders.Application.Payments;

/// <summary>Walidacja pól wspólnych dla wpłaty i zwrotu: kwota, data, wersja, skrót żądania.</summary>
internal static class EntryInput
{
    private static readonly JsonSerializerOptions HashJson = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>Kwota &gt; 0, najwyżej dwa miejsca po przecinku, w zakresie <c>decimal(18,2)</c>.</summary>
    public static decimal? ValidateAmount(string? text, FieldErrors errors)
    {
        var error = OrderInput.ParseAmount(text, out var amount) switch
        {
            AmountError.None when amount == 0 => EntryMessages.AmountNotPositive,
            AmountError.None => null,
            AmountError.Missing => EntryMessages.AmountRequired,
            AmountError.Invalid => EntryMessages.AmountInvalid,
            AmountError.Negative => EntryMessages.AmountNotPositive,
            AmountError.Scale => EntryMessages.AmountScale,
            _ => EntryMessages.AmountTooLarge,
        };

        if (error is null)
        {
            return amount;
        }

        errors.Add(EntryFieldKeys.Amount, error);
        return null;
    }

    /// <summary>Data <c>yyyy-MM-dd</c>, nie późniejsza niż dzisiaj według Europe/Warsaw; wcześniejsza niż zamówienie jest dozwolona.</summary>
    public static DateOnly? ValidateDate(string? text, DateTimeOffset now, FieldErrors errors)
    {
        if (OrderInput.Text(text) is not { } dateText)
        {
            errors.Add(EntryFieldKeys.Date, EntryMessages.DateRequired);
            return null;
        }

        if (!OrderInput.TryParseDate(dateText, out var date))
        {
            errors.Add(EntryFieldKeys.Date, EntryMessages.DateInvalid);
            return null;
        }

        if (date > WarsawTime.Today(now))
        {
            errors.Add(EntryFieldKeys.Date, EntryMessages.DateInFuture);
            return null;
        }

        return date;
    }

    /// <summary>Wersja wpisu z żądania (<c>PUT</c>, <c>DELETE</c>); błąd pod kluczem <c>version</c>.</summary>
    public static byte[]? ValidateVersion(string? text, FieldErrors errors) =>
        OrderVersion.Validate(text, errors, EntryMessages.VersionRequired, EntryMessages.VersionInvalid);

    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>SHA-256 z JSON modelu skrótu (stała kolejność właściwości, kwota kanoniczna, kody słowników).</summary>
    public static byte[] Hash<TModel>(TModel model) => SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(model, HashJson));
}

/// <summary>Klucze błędów walidacji wpisów: ścieżki JSON pól żądania.</summary>
public static class EntryFieldKeys
{
    public const string Kind = "kind";
    public const string Amount = "amount";
    public const string Date = "date";
    public const string Method = "method";
    public const string ReceivedBy = "receivedBy";
    public const string Reason = "reason";
    public const string Version = OrderFieldKeys.Version;
}

/// <summary>Komunikaty walidacji wpisów (UI po polsku).</summary>
public static class EntryMessages
{
    public const string AmountRequired = "Podaj kwotę.";
    public const string AmountInvalid = "Podaj kwotę jako liczbę, np. 1234,50.";
    public const string AmountNotPositive = "Kwota musi być większa od zera.";
    public const string AmountScale = "Kwota może mieć najwyżej dwa miejsca po przecinku.";
    public const string AmountTooLarge = "Kwota jest zbyt duża.";
    public const string DateRequired = "Podaj datę.";
    public const string DateInvalid = "Podaj poprawną datę.";
    public const string DateInFuture = "Data nie może być późniejsza niż dzisiaj.";
    public const string PaymentMethodRequired = "Wybierz metodę wpłaty.";
    public const string PaymentMethodUnknown = "Nieznana metoda wpłaty.";
    public const string RefundMethodRequired = "Wybierz metodę zwrotu.";
    public const string RefundMethodUnknown = "Nieznana metoda zwrotu.";
    public const string ReceivedByOnlyForCash = "Pole „Przyjął” wypełnia się tylko przy płatności gotówką.";
    public const string ReceivedByTooLong = "Pole „Przyjął” może mieć najwyżej 100 znaków.";
    public const string KindRequired = "Wybierz rodzaj zwrotu.";
    public const string KindUnknown = "Nieznany rodzaj zwrotu.";
    public const string ReasonRequired = "Podaj powód zwrotu.";
    public const string ReasonTooLong = "Powód zwrotu może mieć najwyżej 500 znaków.";
    public const string VersionRequired = "Brak wersji wpisu. Wczytaj zamówienie ponownie.";
    public const string VersionInvalid = "Nieprawidłowa wersja wpisu. Wczytaj zamówienie ponownie.";
}
