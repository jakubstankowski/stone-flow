using System.Net;
using System.Text;
using StoneFlow.Integrations.Application.WooCommerce;
using StoneFlow.Integrations.Infrastructure.WooCommerce;
using StoneFlow.Integrations.Infrastructure.WooCommerce.Rest;

namespace StoneFlow.Integrations.Tests.WooCommerce;

/// <summary>SF-13: klient REST v3 z fałszywym handlerem — Basic auth, adresy, klasyfikacja odpowiedzi.</summary>
public sealed class WooCommerceRestClientTests
{
    private const string Key = "ck_test_klucz_123";
    private const string Secret = "cs_test_sekret_456";

    private static readonly WooCommerceOptions Options = new()
    {
        BaseUrl = "https://sklep.example.com",
        ConsumerKey = Key,
        ConsumerSecret = Secret,
    };

    [Fact]
    public async Task Page_request_uses_basic_header_and_never_puts_keys_in_url()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "[]", total: "0", pages: "0"));
        var client = Create(handler);

        await client.GetOrdersPageAsync(
            new RestPageRequest(BackfillSet.Any, new DateTime(2026, 11, 2, 23, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 6, 21, 50, 0, DateTimeKind.Utc), 2),
            TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Key}:{Secret}")), request.Headers.Authorization?.Parameter);
        var url = request.RequestUri!.AbsoluteUri;
        Assert.DoesNotContain(Key, url, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, url, StringComparison.Ordinal);
        Assert.DoesNotContain("consumer_", url, StringComparison.Ordinal);
        Assert.Equal(
            "https://sklep.example.com/wp-json/wc/v3/orders?status=any&dates_are_gmt=true&modified_after=2026-11-02T23%3A00%3A00"
            + "&modified_before=2026-11-06T21%3A50%3A00&orderby=modified&order=asc&per_page=100&page=2",
            url);
    }

    [Fact]
    public async Task Trash_set_has_no_date_filters()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "[]"));

        await Create(handler).GetOrdersPageAsync(new RestPageRequest(BackfillSet.Trash, null, null, 1), TestContext.Current.CancellationToken);

        Assert.Equal(
            "https://sklep.example.com/wp-json/wc/v3/orders?status=trash&orderby=modified&order=asc&per_page=100&page=1",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Page_returns_items_and_pagination_headers()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """[{ "id": 1 }, { "id": 2 }]""", total: "150", pages: "2"));

        var result = await Create(handler).GetOrdersPageAsync(new RestPageRequest(BackfillSet.Any, null, null, 1), TestContext.Current.CancellationToken);

        var loaded = Assert.IsType<RestPageResult.Loaded>(result);
        Assert.Equal(2, loaded.Orders.Count);
        Assert.Equal("""{ "id": 2 }""", Encoding.UTF8.GetString(loaded.Orders[1]));
        Assert.Equal(150, loaded.Total);
        Assert.Equal(2, loaded.TotalPages);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "{}", "Sklep odpowiedział HTTP 503 na stronie 3")]
    [InlineData(HttpStatusCode.Unauthorized, """{ "code": "woocommerce_rest_cannot_view", "message": "ck_test_klucz_123" }""", "Sklep odrzucił klucze REST (HTTP 401)")]
    [InlineData(HttpStatusCode.OK, """{ "id": 1 }""", "Nieczytelna odpowiedź sklepu na stronie 3")]
    [InlineData(HttpStatusCode.OK, "<html>Jan Testowy</html>", "Nieczytelna odpowiedź sklepu na stronie 3")]
    public async Task Page_failures_have_controlled_messages(HttpStatusCode status, string body, string message)
    {
        var handler = new StubHandler(_ => Json(status, body));

        var result = await Create(handler).GetOrdersPageAsync(new RestPageRequest(BackfillSet.Any, null, null, 3), TestContext.Current.CancellationToken);

        Assert.Equal(message, Assert.IsType<RestPageResult.Failed>(result).Message);
    }

    [Fact]
    public async Task Timeout_and_connection_errors_are_controlled_failures()
    {
        var timeout = Create(new StubHandler(_ => throw new TaskCanceledException("https://sklep.example.com/?x")));
        var refused = Create(new StubHandler(_ => throw new HttpRequestException("sklep.example.com:443 Jan Testowy")));

        var timedOut = await timeout.GetOrderAsync("1001", TestContext.Current.CancellationToken);
        var noConnection = await refused.GetOrdersPageAsync(new RestPageRequest(BackfillSet.Any, null, null, 1), TestContext.Current.CancellationToken);

        Assert.Equal("Przekroczono limit czasu", Assert.IsType<RestOrderResult.Failed>(timedOut).Message);
        Assert.Equal("Brak połączenia ze sklepem albo przerwana odpowiedź", Assert.IsType<RestPageResult.Failed>(noConnection).Message);
    }

    [Theory]
    [InlineData("""{ "code": "woocommerce_rest_shop_order_invalid_id", "message": "Nieprawidłowy ID." }""")]
    [InlineData("""{ "code": "woocommerce_rest_invalid_id", "message": "Nieprawidłowy ID." }""")]
    public async Task Not_found_with_woocommerce_code_means_order_is_missing(string body)
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, body));

        var result = await Create(handler).GetOrderAsync("1001", TestContext.Current.CancellationToken);

        Assert.IsType<RestOrderResult.NotFound>(result);
        Assert.Equal("https://sklep.example.com/wp-json/wc/v3/orders/1001", Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData("""{ "code": "rest_no_route", "message": "Nie znaleziono." }""")]
    [InlineData("<html>Nie znaleziono</html>")]
    [InlineData("")]
    public async Task Other_not_found_is_a_failure(string body)
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, body));

        var result = await Create(handler).GetOrderAsync("1001", TestContext.Current.CancellationToken);

        Assert.Equal("Sklep odpowiedział HTTP 404", Assert.IsType<RestOrderResult.Failed>(result).Message);
    }

    [Fact]
    public async Task Found_order_returns_body()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{ "id": 1001 }"""));

        var result = await Create(handler).GetOrderAsync("1001", TestContext.Current.CancellationToken);

        Assert.Equal("""{ "id": 1001 }""", Encoding.UTF8.GetString(Assert.IsType<RestOrderResult.Found>(result).Body));
    }

    [Theory]
    [InlineData("https://sklep.example.com", "ck", "cs", true)]
    [InlineData(null, null, null, true)]
    [InlineData("https://sklep.example.com", "ck", null, false)]
    [InlineData("http://sklep.example.com", "ck", "cs", false)]
    [InlineData("https://sklep.example.com/?consumer_key=ck", "ck", "cs", false)]
    public void Options_require_full_rest_triple_over_https(string? baseUrl, string? key, string? secret, bool valid)
    {
        var options = new WooCommerceOptions { BaseUrl = baseUrl, ConsumerKey = key, ConsumerSecret = secret };

        Assert.Equal(valid, new WooCommerceOptionsValidator().Validate(null, options).Succeeded);
    }

    private static WooCommerceRestClient Create(StubHandler handler)
    {
        var http = new HttpClient(handler);
        WooCommerceRestClient.Configure(http, Options);
        return new WooCommerceRestClient(http);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body, string? total = null, string? pages = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (total is not null)
        {
            response.Headers.Add("X-WP-Total", total);
        }

        if (pages is not null)
        {
            response.Headers.Add("X-WP-TotalPages", pages);
        }

        return response;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }
}
