using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using StoneFlow.Integrations.Application.WooCommerce;

namespace StoneFlow.Integrations.Infrastructure.WooCommerce.Rest;

/// <summary>
/// Klient WooCommerce REST v3 (SF-13), tylko odczyt. Klucze wyłącznie w nagłówku
/// <c>Authorization: Basic</c> przez <c>https</c>, nigdy w adresie. Komunikaty błędów składa kod —
/// bez treści odpowiedzi i komunikatów wyjątków. Obcięty przez hosting nagłówek daje <c>401</c>.
/// </summary>
public sealed class WooCommerceRestClient(HttpClient http) : IWooCommerceRestClient
{
    public const string OrdersPath = "wp-json/wc/v3/orders";

    /// <summary>Największa przyjmowana odpowiedź (strona 100 zamówień mieści się z zapasem).</summary>
    public const long MaxResponseBytes = 32 * 1024 * 1024;

    /// <summary>Kody WooCommerce dla <c>404</c> nieistniejącego zamówienia.</summary>
    private static readonly string[] MissingOrderCodes = ["woocommerce_rest_shop_order_invalid_id", "woocommerce_rest_invalid_id"];

    /// <summary>Ustawia adres, limit czasu i nagłówek Basic klienta (rejestracja <c>AddHttpClient</c>).</summary>
    /// <param name="client">Klient do konfiguracji.</param>
    /// <param name="options">Opcje <c>StoneFlow:WooCommerce</c>.</param>
    public static void Configure(HttpClient client, WooCommerceOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        client.Timeout = options.RequestTimeout;
        client.MaxResponseContentBufferSize = MaxResponseBytes;
        if (!options.RestConfigured)
        {
            return;
        }

        var baseUrl = options.BaseUrl!.Trim();
        client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/", UriKind.Absolute);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ConsumerKey!.Trim()}:{options.ConsumerSecret!.Trim()}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<RestPageResult> GetOrdersPageAsync(RestPageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var where = request.Set == BackfillSet.Trash
            ? $"na stronie {request.Page} (kosz)"
            : $"na stronie {request.Page}";

        var (response, failure) = await SendAsync(PageUri(request), cancellationToken);
        if (failure is not null)
        {
            return new RestPageResult.Failed(failure);
        }

        using (response)
        {
            if (!response!.IsSuccessStatusCode)
            {
                return new RestPageResult.Failed(StatusMessage(response.StatusCode, where));
            }

            if (Items(await ReadAsync(response, cancellationToken)) is not { } items)
            {
                return new RestPageResult.Failed($"Nieczytelna odpowiedź sklepu {where}");
            }

            return new RestPageResult.Loaded(items, Header(response, "X-WP-Total"), Header(response, "X-WP-TotalPages"));
        }
    }

    public async Task<RestOrderResult> GetOrderAsync(string externalId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        var uri = new Uri($"{OrdersPath}/{Uri.EscapeDataString(externalId)}", UriKind.Relative);
        var (response, failure) = await SendAsync(uri, cancellationToken);
        if (failure is not null)
        {
            return new RestOrderResult.Failed(failure);
        }

        using (response)
        {
            if (response!.StatusCode == HttpStatusCode.NotFound)
            {
                return IsMissingOrder(await ReadAsync(response, cancellationToken))
                    ? new RestOrderResult.NotFound()
                    : new RestOrderResult.Failed(StatusMessage(response.StatusCode, null));
            }

            return response.IsSuccessStatusCode
                ? new RestOrderResult.Found(await ReadAsync(response, cancellationToken))
                : new RestOrderResult.Failed(StatusMessage(response.StatusCode, null));
        }
    }

    private static Uri PageUri(RestPageRequest request)
    {
        var query = new StringBuilder(OrdersPath);
        if (request.Set == BackfillSet.Trash)
        {
            query.Append("?status=trash");
        }
        else
        {
            query.Append("?status=any&dates_are_gmt=true");
            if (request.ModifiedAfterUtc is { } after)
            {
                query.Append("&modified_after=").Append(Date(after));
            }

            if (request.ModifiedBeforeUtc is { } before)
            {
                query.Append("&modified_before=").Append(Date(before));
            }
        }

        query.Append(CultureInfo.InvariantCulture, $"&orderby=modified&order=asc&per_page={Application.WooCommerce.Backfill.WooCommerceBackfill.PageSize}&page={request.Page}");
        return new Uri(query.ToString(), UriKind.Relative);
    }

    private static string Date(DateTime value) => Uri.EscapeDataString(value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));

    /// <summary>
    /// Wysyłka z buforowaniem treści (limit <see cref="MaxResponseBytes"/>); brak połączenia, przerwana
    /// albo za duża odpowiedź i limit czasu jako kontrolowany komunikat. Anulowanie przebiegu przechodzi dalej.
    /// </summary>
    private async Task<(HttpResponseMessage? Response, string? Failure)> SendAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            return (await http.GetAsync(uri, cancellationToken), null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, "Przekroczono limit czasu");
        }
        catch (HttpRequestException)
        {
            return (null, "Brak połączenia ze sklepem albo przerwana odpowiedź");
        }
    }

    private static async Task<byte[]> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadAsByteArrayAsync(cancellationToken);

    private static string StatusMessage(HttpStatusCode status, string? where)
    {
        var code = ((int)status).ToString(CultureInfo.InvariantCulture);
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return $"Sklep odrzucił klucze REST (HTTP {code})";
        }

        return where is null ? $"Sklep odpowiedział HTTP {code}" : $"Sklep odpowiedział HTTP {code} {where}";
    }

    /// <summary>Elementy tablicy JSON jako osobne treści; inna treść → <c>null</c>.</summary>
    private static List<byte[]>? Items(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return [.. document.RootElement.EnumerateArray().Select(item => Encoding.UTF8.GetBytes(item.GetRawText()))];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsMissingOrder(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
                && MissingOrderCodes.Contains(code.GetString(), StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
        && int.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
