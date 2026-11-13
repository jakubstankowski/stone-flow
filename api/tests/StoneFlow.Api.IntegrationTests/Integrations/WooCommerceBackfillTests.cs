using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Api.IntegrationTests.Orders;
using StoneFlow.Api.IntegrationTests.Scheduling;
using StoneFlow.Integrations.Application.WooCommerce;
using StoneFlow.Integrations.Application.WooCommerce.Backfill;
using StoneFlow.Integrations.Infrastructure.WooCommerce.Rest;
using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Api.IntegrationTests.Integrations;

/// <summary>
/// SF-13: uzupełnianie zamówień WooCommerce przez REST na prawdziwej bazie z fałszywym sklepem
/// (<see cref="FakeShop"/>), historia błędów, „Ponów” i „Synchronizuj teraz”. Dane fikcyjne.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class WooCommerceBackfillTests(SqlServerFixture sqlServer)
{
    private const string Secret = "sekret-testowy-1234567890";
    private const string Key = "ck_test_klucz_rest_123";
    private const string KeySecret = "cs_test_sekret_rest_456";
    private const string Base = "/api/integrations/woocommerce";

    private static readonly Dictionary<string, string?> Configured = new()
    {
        ["StoneFlow:WooCommerce:WebhookSecret"] = Secret,
        ["StoneFlow:WooCommerce:BaseUrl"] = "https://sklep.example.com",
        ["StoneFlow:WooCommerce:ConsumerKey"] = Key,
        ["StoneFlow:WooCommerce:ConsumerSecret"] = KeySecret,
    };

    private readonly FakeShop _shop = new();

    [Fact]
    public async Task Missed_webhook_is_backfilled_from_launch_date_once()
    {
        await using var test = await CreateAsync();
        _shop.Put(1001, modified: "2026-11-05T10:00:00");

        Assert.Equal(JobRunStatus.Succeeded, await RunAsync(test));
        test.Time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(JobRunStatus.Succeeded, await RunAsync(test));

        Assert.Equal(1, await ShopOrderCountAsync(test));
        Assert.Contains("modified_after=2026-11-02T23%3A00%3A00", _shop.ListRequests("any")[0], StringComparison.Ordinal);
        Assert.Contains("modified_before=2026-11-05T11%3A50%3A00", _shop.ListRequests("any")[0], StringComparison.Ordinal);
        var status = await test.GetJsonAsync($"{Base}/status");
        Assert.True(status.GetProperty("configured").GetBoolean());
        Assert.Equal("succeeded", status.GetProperty("lastRun").GetProperty("status").GetString());
        Assert.Equal("manual", status.GetProperty("lastRun").GetProperty("source").GetString());
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 12, 50, 0, TimeSpan.Zero), status.GetProperty("lastSuccessAtUtc").GetDateTimeOffset());
        Assert.False(status.GetProperty("isRunning").GetBoolean());
        Assert.Equal(0, status.GetProperty("openErrors").GetInt32());
    }

    [Fact]
    public async Task Unavailable_shop_fails_runs_keeps_cursor_and_next_run_recovers_from_it()
    {
        await using var test = await CreateAsync();
        Assert.Equal(JobRunStatus.Succeeded, await RunAsync(test));

        test.Time.Advance(TimeSpan.FromDays(3));
        _shop.ListFailure = HttpStatusCode.ServiceUnavailable;
        Assert.Equal(JobRunStatus.Failed, await RunAsync(test));
        test.Time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(JobRunStatus.Failed, await RunAsync(test));

        // Zegar przesunięty o dni: sesja wygasła, więc przebiegi odczytane z bazy.
        var failed = await test.RowsAsync(
            "SELECT Status, Message FROM integrations.WooCommerceSyncRuns WHERE Status = N'failed' ORDER BY StartedAtUtc",
            r => (Status: r.GetString(0), Message: r.GetString(1)));
        Assert.Equal(2, failed.Count);
        Assert.All(failed, run => Assert.Equal("Sklep odpowiedział HTTP 503 na stronie 1", run.Message));
        Assert.Equal(new DateTime(2026, 11, 5, 11, 50, 0), await CursorAsync(test));

        _shop.ListFailure = null;
        _shop.Put(1002, modified: "2026-11-07T10:00:00");
        Assert.Equal(JobRunStatus.Succeeded, await RunAsync(test));

        Assert.Contains("modified_after=2026-11-05T11%3A40%3A00", _shop.ListRequests("any")[^1], StringComparison.Ordinal);
        Assert.Equal(1, await ShopOrderCountAsync(test));
        Assert.Equal(
            "WooCommerceBackfillException: Sklep odpowiedział HTTP 503 na stronie 1",
            await test.ScalarAsync<string>("SELECT TOP 1 Error FROM scheduling.JobRuns WHERE Status = N'failed' ORDER BY StartedAtUtc"));
    }

    [Fact]
    public async Task Order_moved_to_trash_without_webhook_is_cancelled()
    {
        await using var test = await CreateAsync();
        _shop.Put(1001, modified: "2026-11-05T10:00:00");
        await RunAsync(test);

        // Data modyfikacji poza oknem kolejnego przebiegu: zestaw kosza jest pobierany bez filtrów dat.
        _shop.Put(1001, status: "trash", modified: "2026-11-05T11:00:00");
        test.Time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(JobRunStatus.Succeeded, await RunAsync(test));

        var order = await ShopOrderAsync(test, "WC-1001");
        Assert.Equal("cancelled", order.GetProperty("fulfillmentStatus").GetString());
        Assert.All(_shop.ListRequests("trash"), u => Assert.DoesNotContain("modified_", u, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unreadable_order_opens_error_and_retry_closes_it()
    {
        await using var test = await CreateAsync();
        _shop.Put(1001, modified: "2026-11-05T10:00:00", unreadable: true);
        _shop.Put(1002, modified: "2026-11-05T10:30:00");

        Assert.Equal(JobRunStatus.Succeeded, await RunAsync(test));

        var error = Assert.Single((await test.GetJsonAsync($"{Base}/errors")).GetProperty("items").EnumerateArray());
        Assert.Equal("1001", error.GetProperty("externalId").GetString());
        Assert.Equal("backfill", error.GetProperty("source").GetString());
        Assert.Equal("open", error.GetProperty("status").GetString());
        Assert.Equal("Nieczytelna treść zamówienia: brak wymaganych pól zamówienia 1001", error.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, error.GetProperty("orderNumber").ValueKind);
        Assert.Equal(1, (await test.GetJsonAsync($"{Base}/status")).GetProperty("openErrors").GetInt32());
        Assert.Equal(1, (await test.GetJsonAsync($"{Base}/status")).GetProperty("lastRun").GetProperty("errors").GetInt32());

        _shop.Put(1001, modified: "2026-11-05T11:00:00");
        using var retry = await test.SendAsync(HttpMethod.Post, $"{Base}/errors/{error.GetProperty("id").GetGuid()}/retry");

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var closed = await AuthClient.ProblemAsync(retry);
        Assert.Equal("closed", closed.GetProperty("status").GetString());
        Assert.Equal("retry", closed.GetProperty("closedBy").GetString());
        Assert.Equal("WC-1001", closed.GetProperty("orderNumber").GetString());
        Assert.NotNull(closed.GetProperty("closedAtUtc").GetString());
        Assert.Equal(2, await ShopOrderCountAsync(test));

        using var again = await test.SendAsync(HttpMethod.Post, $"{Base}/errors/{error.GetProperty("id").GetGuid()}/retry");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("error_closed", (await AuthClient.ProblemAsync(again)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Retry_of_order_missing_in_shop_applies_deletion()
    {
        await using var test = await CreateAsync();
        _shop.Put(1001, modified: "2026-11-05T10:00:00");
        await RunAsync(test);
        Assert.Equal(HttpStatusCode.OK, await WebhookAsync(test, "order.updated", """{ "id": 1001, "status": "processing" }"""));
        var id = await OpenErrorIdAsync(test, "1001");

        _shop.Remove(1001);
        using var retry = await test.SendAsync(HttpMethod.Post, $"{Base}/errors/{id}/retry");

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("closed", (await AuthClient.ProblemAsync(retry)).GetProperty("status").GetString());
        var order = await ShopOrderAsync(test, "WC-1001");
        Assert.Equal("cancelled", order.GetProperty("fulfillmentStatus").GetString());
        Assert.NotEqual(JsonValueKind.Null, order.GetProperty("shopDeletedAtUtc").ValueKind);
    }

    [Fact]
    public async Task Retry_with_other_not_found_keeps_error_open_with_502()
    {
        await using var test = await CreateAsync();
        Assert.Equal(HttpStatusCode.OK, await WebhookAsync(test, "order.created", """{ "id": 1001, "status": "processing" }"""));
        var id = await OpenErrorIdAsync(test, "1001");
        _shop.SingleOverride = _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("<html>Jan Testowy</html>") };

        using var retry = await test.SendAsync(HttpMethod.Post, $"{Base}/errors/{id}/retry");

        Assert.Equal(HttpStatusCode.BadGateway, retry.StatusCode);
        Assert.Equal("retry_failed", (await AuthClient.ProblemAsync(retry)).GetProperty("code").GetString());
        var error = Assert.Single((await test.GetJsonAsync($"{Base}/errors")).GetProperty("items").EnumerateArray());
        Assert.Equal(2, error.GetProperty("attempts").GetInt32());
        Assert.Equal("Sklep odpowiedział HTTP 404", error.GetProperty("message").GetString());
        Assert.Equal(retry.Headers.GetValues("X-Correlation-ID").Single(), error.GetProperty("context").GetString());
        Assert.Equal(0, await ShopOrderCountAsync(test));

        using var missing = await test.SendAsync(HttpMethod.Post, $"{Base}/errors/{Guid.NewGuid()}/retry");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("error_not_found", (await AuthClient.ProblemAsync(missing)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Successful_webhook_closes_error_but_older_delivery_does_not()
    {
        await using var test = await CreateAsync();
        Assert.Equal(
            HttpStatusCode.OK,
            await WebhookAsync(test, "order.updated", """{ "id": 2002, "status": "processing", "date_modified_gmt": "2026-11-05T11:00:00" }"""));

        Assert.Equal(HttpStatusCode.OK, await WebhookAsync(test, "order.created", FakeShop.Json(2002, "processing", "2026-11-05T10:00:00")));
        var open = Assert.Single((await test.GetJsonAsync($"{Base}/errors")).GetProperty("items").EnumerateArray());
        Assert.Equal("webhook", open.GetProperty("source").GetString());
        Assert.Equal("order.updated, dostawa 42", open.GetProperty("context").GetString());
        Assert.Equal("WC-2002", open.GetProperty("orderNumber").GetString());

        Assert.Equal(HttpStatusCode.OK, await WebhookAsync(test, "order.updated", FakeShop.Json(2002, "processing", "2026-11-05T11:30:00")));

        var closed = Assert.Single((await test.GetJsonAsync($"{Base}/errors?status=closed")).GetProperty("items").EnumerateArray());
        Assert.Equal("webhook", closed.GetProperty("closedBy").GetString());
        Assert.Equal(0, (await test.GetJsonAsync($"{Base}/errors")).GetProperty("total").GetInt32());
        Assert.Equal(1, (await test.GetJsonAsync($"{Base}/errors?status=all")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Failing_error_history_does_not_change_webhook_status_codes()
    {
        var saveFails = false;
        await using var test = await OrdersTest.CreateAsync(
            sqlServer,
            hooks: new StoreHooks { BeforeAdd = _ => saveFails ? throw new InvalidOperationException("Symulowany błąd zapisu.") : Task.CompletedTask },
            configureServices: services =>
            {
                UseShop(services);
                services.RemoveAll<IWooCommerceOrderErrors>();
                services.AddScoped<IWooCommerceOrderErrors, ThrowingOrderErrors>();
            },
            settings: Configured);

        Assert.Equal(HttpStatusCode.OK, await WebhookAsync(test, "order.created", """{ "id": 1001, "status": "processing" }"""));
        Assert.Equal(HttpStatusCode.OK, await WebhookAsync(test, "order.created", FakeShop.Json(1001, "processing", "2026-11-05T10:00:00")));
        saveFails = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, await WebhookAsync(test, "order.created", FakeShop.Json(1002, "processing", "2026-11-05T10:00:00")));

        Assert.Equal(1, await ShopOrderCountAsync(test));
        Assert.Contains(test.Factory.Logs.GetSnapshot(), l => l.Id.Id == 7306);
    }

    [Fact]
    public async Task Retry_with_erp_save_failure_returns_503_and_keeps_error_open()
    {
        var saveFails = false;
        await using var test = await OrdersTest.CreateAsync(
            sqlServer,
            hooks: new StoreHooks { BeforeAdd = _ => saveFails ? throw new InvalidOperationException("Symulowany błąd zapisu.") : Task.CompletedTask },
            configureServices: UseShop,
            settings: Configured);
        Assert.Equal(HttpStatusCode.OK, await WebhookAsync(test, "order.created", """{ "id": 1001, "status": "processing" }"""));
        var id = await OpenErrorIdAsync(test, "1001");
        _shop.Put(1001, modified: "2026-11-05T10:00:00");
        saveFails = true;

        using var retry = await test.SendAsync(HttpMethod.Post, $"{Base}/errors/{id}/retry");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, retry.StatusCode);
        Assert.Equal("retry_unavailable", (await AuthClient.ProblemAsync(retry)).GetProperty("code").GetString());
        var error = Assert.Single((await test.GetJsonAsync($"{Base}/errors")).GetProperty("items").EnumerateArray());
        Assert.Equal(2, error.GetProperty("attempts").GetInt32());
        Assert.Equal("Błąd zapisu zamówienia (InvalidOperationException)", error.GetProperty("message").GetString());
        Assert.Equal(0, await ShopOrderCountAsync(test));
    }

    [Fact]
    public async Task Sync_now_while_scheduled_run_holds_the_lease_gets_409()
    {
        await using var test = await CreateAsync();
        await test.ExecuteSqlAsync(
            "INSERT INTO scheduling.Jobs (JobKey, RegisteredAtUtc, ConsecutiveFailures, LeaseRunId, LeaseExpiresAtUtc) "
            + $"VALUES (N'{WooCommerceBackfill.JobKey}', '2026-11-04T00:00:00', 0, NEWID(), '2026-11-05T13:00:00');");

        using var sync = await test.SendAsync(HttpMethod.Post, $"{Base}/sync");

        Assert.Equal(HttpStatusCode.Conflict, sync.StatusCode);
        Assert.Equal("sync_in_progress", (await AuthClient.ProblemAsync(sync)).GetProperty("code").GetString());
        Assert.True((await test.GetJsonAsync($"{Base}/status")).GetProperty("isRunning").GetBoolean());
        Assert.Empty(_shop.Requests);
    }

    [Fact]
    public async Task Concurrent_failures_of_one_order_give_one_open_error()
    {
        await using var test = await CreateAsync();

        var statuses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            WebhookAsync(test, "order.updated", """{ "id": 3003, "status": "processing" }""")));

        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.OK, s));
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM integrations.WooCommerceOrderErrors"));
        Assert.Equal(4, await test.ScalarAsync<int>("SELECT Attempts FROM integrations.WooCommerceOrderErrors"));
    }

    [Fact]
    public async Task Sync_now_runs_in_background_once_and_second_request_gets_409()
    {
        await using var test = await CreateAsync();
        _shop.Put(1001, modified: "2026-11-05T10:00:00");
        _shop.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var first = await test.SendAsync(HttpMethod.Post, $"{Base}/sync");
        using var second = await test.SendAsync(HttpMethod.Post, $"{Base}/sync");

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("sync_in_progress", (await AuthClient.ProblemAsync(second)).GetProperty("code").GetString());
        Assert.True((await test.GetJsonAsync($"{Base}/status")).GetProperty("isRunning").GetBoolean());

        _shop.Gate.SetResult();
        var status = await Waiting.ForAsync(
            () => test.GetJsonAsync($"{Base}/status"),
            s => !s.GetProperty("isRunning").GetBoolean() && s.GetProperty("lastRun").ValueKind == JsonValueKind.Object);

        Assert.Equal("succeeded", status.GetProperty("lastRun").GetProperty("status").GetString());
        Assert.Equal("manual", status.GetProperty("lastRun").GetProperty("source").GetString());
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM integrations.WooCommerceSyncRuns"));
        Assert.Equal(1, await ShopOrderCountAsync(test));
    }

    [Fact]
    public async Task Without_rest_keys_sync_and_retry_are_not_configured_and_job_does_nothing()
    {
        await using var test = await OrdersTest.CreateAsync(
            sqlServer,
            configureServices: UseShop,
            settings: new Dictionary<string, string?> { ["StoneFlow:WooCommerce:WebhookSecret"] = Secret });
        Assert.Equal(HttpStatusCode.OK, await WebhookAsync(test, "order.created", """{ "id": 1001, "status": "processing" }"""));
        var id = await OpenErrorIdAsync(test, "1001");

        using var sync = await test.SendAsync(HttpMethod.Post, $"{Base}/sync");
        using var retry = await test.SendAsync(HttpMethod.Post, $"{Base}/errors/{id}/retry");

        Assert.Equal("integration_not_configured", (await AuthClient.ProblemAsync(sync)).GetProperty("code").GetString());
        Assert.Equal("integration_not_configured", (await AuthClient.ProblemAsync(retry)).GetProperty("code").GetString());
        Assert.Equal(JobRunStatus.Succeeded, await RunAsync(test));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM integrations.WooCommerceSyncRuns"));
        Assert.Empty(_shop.Requests);
        var status = await test.GetJsonAsync($"{Base}/status");
        Assert.False(status.GetProperty("configured").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("lastRun").ValueKind);
    }

    [Fact]
    public async Task Error_list_validates_query()
    {
        await using var test = await CreateAsync();

        using var response = await test.SendAsync(HttpMethod.Get, $"{Base}/errors?status=bad&page=0&pageSize=101");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await AuthClient.ProblemAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("status", out _));
        Assert.True(errors.TryGetProperty("page", out _));
        Assert.True(errors.TryGetProperty("pageSize", out _));
    }

    [Fact]
    public async Task Endpoints_require_session_and_csrf()
    {
        await using var test = await CreateAsync();
        using var anonymous = test.Factory.CreateClient();

        foreach (var (method, path) in new[] { (HttpMethod.Get, "/status"), (HttpMethod.Get, "/errors"), (HttpMethod.Post, "/sync"), (HttpMethod.Post, $"/errors/{Guid.NewGuid()}/retry") })
        {
            using var request = new HttpRequestMessage(method, new Uri(Base + path, UriKind.Relative));
            using var response = await anonymous.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var withoutCsrf = await test.SendAsync(HttpMethod.Post, $"{Base}/sync", withCsrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, withoutCsrf.StatusCode);
        Assert.Empty(_shop.Requests);
    }

    [Fact]
    public async Task Keys_stay_in_authorization_header_and_out_of_logs_and_history()
    {
        await using var test = await CreateAsync();
        _shop.Put(1001, modified: "2026-11-05T10:00:00", unreadable: true);
        await RunAsync(test);
        _shop.ListFailure = HttpStatusCode.Unauthorized;
        test.Time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(JobRunStatus.Failed, await RunAsync(test));

        Assert.NotEmpty(_shop.Requests);
        Assert.All(_shop.Requests, r =>
        {
            Assert.Equal($"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Key}:{KeySecret}"))}", r.Authorization);
            Assert.DoesNotContain(Key, r.Uri, StringComparison.Ordinal);
            Assert.DoesNotContain(KeySecret, r.Uri, StringComparison.Ordinal);
        });
        var stored = string.Join(
            '\n',
            await test.RowsAsync("SELECT Message + N' ' + Context FROM integrations.WooCommerceOrderErrors UNION ALL SELECT ISNULL(Message, N'') FROM integrations.WooCommerceSyncRuns UNION ALL SELECT ISNULL(Error, N'') FROM scheduling.JobRuns", r => r.GetString(0)));
        Assert.Contains("Sklep odrzucił klucze REST (HTTP 401)", stored, StringComparison.Ordinal);
        var logs = string.Join('\n', test.Factory.Logs.GetSnapshot().Select(l => l.Message + " " + l.Exception));
        foreach (var text in new[] { stored, logs })
        {
            Assert.DoesNotContain(Key, text, StringComparison.Ordinal);
            Assert.DoesNotContain(KeySecret, text, StringComparison.Ordinal);
            Assert.DoesNotContain("Testowy", text, StringComparison.Ordinal);
            Assert.DoesNotContain("jan.testowy", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Backfill_and_retry_after_anonymization_do_not_restore_customer_data()
    {
        await using var test = await OrdersTest.CreateAsync(
            sqlServer, configureServices: UseShop, settings: AnonymizationTestSupport.Retention(extra: Configured));
        _shop.Put(1001, modified: "2026-11-05T10:00:00");
        Assert.Equal(JobRunStatus.Succeeded, await RunAsync(test));
        var id = (await ShopOrderAsync(test, "WC-1001")).GetProperty("id").GetGuid();
        await AnonymizationTestSupport.MoveClockAndLoginAsync(test, new DateTimeOffset(2026, 12, 10, 12, 0, 0, TimeSpan.Zero));
        await AnonymizationTestSupport.AnonymizeAsync(test, id);

        _shop.Put(1001, status: "completed", modified: "2026-12-10T10:00:00");
        Assert.Equal(JobRunStatus.Succeeded, await RunAsync(test));

        var backfilled = await ShopOrderAsync(test, "WC-1001");
        Assert.Equal("completed", backfilled.GetProperty("shopStatus").GetString());
        Assert.Equal(AnonymizationTestSupport.Marker, backfilled.GetProperty("customer").GetProperty("name").GetString());

        _shop.Put(1001, status: "completed", modified: "2026-12-10T12:00:00", unreadable: true);
        test.Time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(JobRunStatus.Succeeded, await RunAsync(test));
        var errorId = await OpenErrorIdAsync(test, "1001");
        _shop.Put(1001, status: "on-hold", modified: "2026-12-10T12:30:00");
        using var retry = await test.SendAsync(HttpMethod.Post, $"{Base}/errors/{errorId}/retry");

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("closed", (await AuthClient.ProblemAsync(retry)).GetProperty("status").GetString());
        var retried = await ShopOrderAsync(test, "WC-1001");
        Assert.Equal("on-hold", retried.GetProperty("shopStatus").GetString());
        Assert.Equal(AnonymizationTestSupport.Marker, retried.GetProperty("customer").GetProperty("name").GetString());
        Assert.Equal(0, await AnonymizationTestSupport.OccurrencesAsync(test, "Testowy"));
    }

    private Task<OrdersTest> CreateAsync() => OrdersTest.CreateAsync(sqlServer, configureServices: UseShop, settings: Configured);

    private void UseShop(IServiceCollection services) =>
        services.AddHttpClient<IWooCommerceRestClient, WooCommerceRestClient>().ConfigurePrimaryHttpMessageHandler(() => _shop);

    /// <summary>Przebieg zadania przez runner harmonogramu (ta sama dzierżawa co o 23:00).</summary>
    private static async Task<JobRunStatus> RunAsync(OrdersTest test)
    {
        var outcome = await test.Factory.Services.GetRequiredService<IJobRunner>()
            .TryRunNowAsync(WooCommerceBackfill.JobKey, TestContext.Current.CancellationToken);
        return Assert.IsType<JobRunOutcome.Completed>(outcome).Status;
    }

    private static Task<int> ShopOrderCountAsync(OrdersTest test) =>
        test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Orders WHERE Source = N'WooCommerce'");

    private static async Task<DateTime?> CursorAsync(OrdersTest test) =>
        (await test.RowsAsync("SELECT LastSuccessCursorUtc FROM integrations.WooCommerceSyncState", r => r.IsDBNull(0) ? (DateTime?)null : r.GetDateTime(0))).SingleOrDefault();

    private static async Task<Guid> OpenErrorIdAsync(OrdersTest test, string externalId) =>
        Assert.Single((await test.GetJsonAsync($"{Base}/errors")).GetProperty("items").EnumerateArray(), e => e.GetProperty("externalId").GetString() == externalId)
            .GetProperty("id").GetGuid();

    private static async Task<JsonElement> ShopOrderAsync(OrdersTest test, string number)
    {
        var list = await test.GetJsonAsync("/api/orders?includeHiddenShopStatuses=true");
        var id = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("number").GetString() == number).GetProperty("id").GetGuid();
        return await test.GetJsonAsync($"/api/orders/{id}");
    }

    private static async Task<HttpStatusCode> WebhookAsync(OrdersTest test, string topic, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/webhooks/woocommerce/orders", UriKind.Relative))
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-WC-Webhook-Topic", topic);
        request.Headers.Add("X-WC-Webhook-Delivery-ID", "42");
        request.Headers.Add("X-WC-Webhook-Signature", WebhookSignature.Sign(bytes, Secret));
        using var client = test.Factory.CreateClient();
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }
}

/// <summary>Historia błędów z awarią bazy przy każdym zapisie (wymóg: nie zmienia wyniku webhooka).</summary>
internal sealed class ThrowingOrderErrors : IWooCommerceOrderErrors
{
    public Task RecordAsync(OrderFailure failure, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Symulowany błąd zapisu historii.");

    public Task<bool> CloseAsync(string externalId, DateTime? modifiedAtUtc, bool isDeletion, string closedBy, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Symulowany błąd zapisu historii.");

    public Task<OrderErrorView?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<OrderErrorPage> ListAsync(string? status, int page, int pageSize, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<int> CountOpenAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}

/// <summary>Żądanie do sklepu: adres i nagłówek <c>Authorization</c>.</summary>
internal sealed record ShopRequest(string Uri, string? Authorization);

/// <summary>
/// Sklep WooCommerce REST v3 w pamięci: lista z filtrami <c>status</c>, <c>modified_after</c>,
/// <c>modified_before</c> i paginacją, pojedyncze zamówienie, 404 z kodem dla nieistniejącego.
/// </summary>
internal sealed class FakeShop : HttpMessageHandler
{
    private readonly ConcurrentDictionary<long, (string Status, string Modified, bool Unreadable)> _orders = new();

    public ConcurrentQueue<ShopRequest> Requests { get; } = new();

    public HttpStatusCode? ListFailure { get; set; }

    public Func<long, HttpResponseMessage>? SingleOverride { get; set; }

    /// <summary>Wstrzymuje odpowiedzi do zwolnienia (przebieg w toku).</summary>
    public TaskCompletionSource? Gate { get; set; }

    public static string Json(long id, string status, string modified, bool unreadable = false) => $$"""
        {
          "id": {{id}}, "status": "{{status}}", "currency": "PLN",
          "date_created_gmt": "2026-11-04T10:00:00", "date_modified_gmt": "{{modified}}"{{(unreadable ? string.Empty : ", \"total\": \"1000.00\"")}},
          "billing": { "first_name": "Jan", "last_name": "Testowy", "email": "jan.testowy@example.com" },
          "meta_data": [], "refunds": []
        }
        """;

    public void Put(long id, string status = "processing", string modified = "2026-11-05T10:00:00", bool unreadable = false) =>
        _orders[id] = (status, modified, unreadable);

    public void Remove(long id) => _orders.TryRemove(id, out _);

    public List<string> ListRequests(string status) =>
        [.. Requests.Select(r => r.Uri).Where(u => u.Contains($"/orders?status={status}", StringComparison.Ordinal))];

    protected override void Dispose(bool disposing)
    {
        // Handler należy do testu; fabryka HttpClient nie może go zwolnić w trakcie testu.
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(new ShopRequest(request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString()));
        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        var path = request.RequestUri.AbsolutePath;
        const string prefix = "/wp-json/wc/v3/orders";
        if (path == prefix)
        {
            return ListFailure is { } failure ? new HttpResponseMessage(failure) { Content = new StringContent("{}") } : List(request.RequestUri);
        }

        var id = long.Parse(path[(prefix.Length + 1)..], CultureInfo.InvariantCulture);
        if (SingleOverride is { } single)
        {
            return single(id);
        }

        return _orders.TryGetValue(id, out var order)
            ? Response(HttpStatusCode.OK, Json(id, order.Status, order.Modified, order.Unreadable))
            : Response(HttpStatusCode.NotFound, """{ "code": "woocommerce_rest_shop_order_invalid_id", "message": "Nieprawidłowy ID.", "data": { "status": 404 } }""");
    }

    private HttpResponseMessage List(Uri uri)
    {
        var query = QueryHelpers.ParseQuery(uri.Query);
        var status = query["status"].ToString();
        DateTime? after = query.TryGetValue("modified_after", out var a) ? Parse(a.ToString()) : null;
        DateTime? before = query.TryGetValue("modified_before", out var b) ? Parse(b.ToString()) : null;
        var perPage = int.Parse(query["per_page"].ToString(), CultureInfo.InvariantCulture);
        var page = int.Parse(query["page"].ToString(), CultureInfo.InvariantCulture);

        var matching = _orders
            .Where(o => status == "trash" ? o.Value.Status == "trash" : o.Value.Status != "trash")
            .Where(o => after is null || Parse(o.Value.Modified) > after)
            .Where(o => before is null || Parse(o.Value.Modified) < before)
            .OrderBy(o => Parse(o.Value.Modified))
            .ThenBy(o => o.Key)
            .ToList();
        var items = matching.Skip((page - 1) * perPage).Take(perPage).Select(o => Json(o.Key, o.Value.Status, o.Value.Modified, o.Value.Unreadable));

        var response = Response(HttpStatusCode.OK, "[" + string.Join(',', items) + "]");
        response.Headers.Add("X-WP-Total", matching.Count.ToString(CultureInfo.InvariantCulture));
        response.Headers.Add("X-WP-TotalPages", ((matching.Count + perPage - 1) / perPage).ToString(CultureInfo.InvariantCulture));
        return response;
    }

    private static DateTime Parse(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
