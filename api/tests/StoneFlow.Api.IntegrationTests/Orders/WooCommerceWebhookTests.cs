using System.Net;
using System.Text;
using System.Text.Json;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Integrations.Application.WooCommerce;

namespace StoneFlow.Api.IntegrationTests.Orders;

/// <summary>
/// SF-12: webhooki WooCommerce na prawdziwej bazie — podpis, idempotencja, kolejność dostaw,
/// jednorazowe przejście na „Anulowane”, usunięcie, audyt, lista i szczegóły. Dane fikcyjne.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class WooCommerceWebhookTests(SqlServerFixture sqlServer)
{
    private const string Secret = "sekret-testowy-1234567890";
    private const string Route = "/webhooks/woocommerce/orders";

    private static readonly Dictionary<string, string?> Enabled = new() { ["StoneFlow:WooCommerce:WebhookSecret"] = Secret };

    private static string Order(
        long id = 1001,
        string status = "processing",
        string modified = "2026-11-05T10:00:00",
        string created = "2026-11-04T10:00:00",
        string total = "1000.00",
        string refunds = "[]") => $$"""
        {
          "id": {{id}}, "status": "{{status}}", "currency": "PLN",
          "date_created_gmt": "{{created}}", "date_modified_gmt": "{{modified}}", "total": "{{total}}",
          "billing": { "first_name": "Jan", "last_name": "Testowy", "company": "", "address_1": "ul. Przykładowa 1",
                       "postcode": "00-950", "city": "Warszawa", "email": "jan.testowy@example.com", "phone": "500600700" },
          "meta_data": [], "refunds": {{refunds}}
        }
        """;

    private static async Task<HttpStatusCode> PostAsync(OrdersTest test, string topic, string body, string? signature = null, bool sign = true)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Route, UriKind.Relative))
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Content.Headers.ContentType = new("application/json");
        if (topic.Length > 0)
        {
            request.Headers.Add("X-WC-Webhook-Topic", topic);
        }

        request.Headers.Add("X-WC-Webhook-Delivery-ID", "42");
        if (sign)
        {
            request.Headers.Add("X-WC-Webhook-Signature", signature ?? WebhookSignature.Sign(bytes, Secret));
        }

        using var client = test.Factory.CreateClient();
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private static Task<OrdersTest> CreateAsync(SqlServerFixture sqlServer, bool enabled = true) =>
        OrdersTest.CreateAsync(sqlServer, settings: enabled ? Enabled : null);

    private static async Task<JsonElement> ShopOrderAsync(OrdersTest test, string number = "WC-1001")
    {
        var list = await test.GetJsonAsync("/api/orders?includeHiddenShopStatuses=true");
        var id = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("number").GetString() == number).GetProperty("id").GetGuid();
        return await test.GetJsonAsync($"/api/orders/{id}");
    }

    private static Task<int> CountAsync(OrdersTest test) =>
        test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Orders WHERE Source = N'WooCommerce'");

    private static Task<int> SyncAuditCountAsync(OrdersTest test) =>
        test.ScalarAsync<int>("SELECT COUNT(*) FROM reporting.AuditEntries WHERE ObjectType = N'Order' AND AuthorKind = N'WooCommerceSync'");

    [Fact]
    public async Task Created_order_is_imported_once_with_audit_and_no_payments()
    {
        await using var test = await CreateAsync(sqlServer);

        Assert.Equal(HttpStatusCode.OK, await PostAsync(test, "order.created", Order()));
        Assert.Equal(HttpStatusCode.OK, await PostAsync(test, "order.created", Order()));
        Assert.Equal(HttpStatusCode.OK, await PostAsync(test, "order.updated", Order()));

        Assert.Equal(1, await CountAsync(test));
        var details = await ShopOrderAsync(test);
        Assert.Equal("wooCommerce", details.GetProperty("source").GetString());
        Assert.Equal("1001", details.GetProperty("externalId").GetString());
        Assert.Equal("shop", details.GetProperty("channel").GetString());
        Assert.Equal("1000.00", details.GetProperty("value").GetString());
        Assert.Equal("inProgress", details.GetProperty("fulfillmentStatus").GetString());
        Assert.Equal("processing", details.GetProperty("shopStatus").GetString());
        Assert.Equal(0, details.GetProperty("payments").GetArrayLength());
        Assert.Equal(0, details.GetProperty("shopWarnings").GetArrayLength());
        Assert.Equal(1, await SyncAuditCountAsync(test));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Payments"));
    }

    [Fact]
    public async Task Concurrent_created_deliveries_give_one_record()
    {
        await using var test = await CreateAsync(sqlServer);

        var statuses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PostAsync(test, "order.created", Order())));

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Equal(1, await CountAsync(test));
    }

    [Fact]
    public async Task Older_delivery_changes_nothing_and_erp_fields_are_kept()
    {
        await using var test = await CreateAsync(sqlServer);
        await PostAsync(test, "order.created", Order(modified: "2026-11-05T10:00:00"));
        await PostAsync(test, "order.updated", Order(modified: "2026-11-05T12:00:00", total: "900.00"));

        Assert.Equal(HttpStatusCode.OK, await PostAsync(test, "order.updated", Order(modified: "2026-11-05T11:00:00", total: "1.00")));

        var details = await ShopOrderAsync(test);
        Assert.Equal("900.00", details.GetProperty("value").GetString());
        Assert.Equal(2, await SyncAuditCountAsync(test));
    }

    [Fact]
    public async Task Order_from_before_launch_creates_no_record()
    {
        await using var test = await CreateAsync(sqlServer);

        Assert.Equal(HttpStatusCode.OK, await PostAsync(test, "order.created", Order(created: "2026-11-01T10:00:00")));

        Assert.Equal(0, await CountAsync(test));
    }

    [Fact]
    public async Task Cancel_in_shop_cancels_once_and_redelivery_after_erp_restore_keeps_status()
    {
        await using var test = await CreateAsync(sqlServer);
        await PostAsync(test, "order.created", Order());
        await PostAsync(test, "order.updated", Order(status: "cancelled", modified: "2026-11-05T11:00:00"));

        var cancelled = await ShopOrderAsync(test);
        Assert.Equal("cancelled", cancelled.GetProperty("fulfillmentStatus").GetString());
        await test.StatusActionAsync(cancelled.GetProperty("id").GetGuid(), "restore", cancelled.GetProperty("version").GetString()!);

        await PostAsync(test, "order.updated", Order(status: "cancelled", modified: "2026-11-05T11:00:00"));

        var restored = await ShopOrderAsync(test);
        Assert.Equal("inProgress", restored.GetProperty("fulfillmentStatus").GetString());
        Assert.Equal(["cancelledInShop"], restored.GetProperty("shopWarnings").EnumerateArray().Select(w => w.GetString()));
    }

    [Fact]
    public async Task Erp_cancel_of_active_order_shows_active_in_shop()
    {
        await using var test = await CreateAsync(sqlServer);
        await PostAsync(test, "order.created", Order());
        var details = await ShopOrderAsync(test);

        await test.StatusActionAsync(details.GetProperty("id").GetGuid(), "cancel", details.GetProperty("version").GetString()!);
        await PostAsync(test, "order.updated", Order(modified: "2026-11-05T12:00:00", total: "950.00"));

        var after = await ShopOrderAsync(test);
        Assert.Equal("cancelled", after.GetProperty("fulfillmentStatus").GetString());
        Assert.Equal("950.00", after.GetProperty("value").GetString());
        Assert.Equal(["activeInShop"], after.GetProperty("shopWarnings").EnumerateArray().Select(w => w.GetString()));
    }

    [Fact]
    public async Task Deletion_cancels_and_late_delivery_from_before_deletion_changes_nothing()
    {
        await using var test = await CreateAsync(sqlServer);
        await PostAsync(test, "order.created", Order());

        Assert.Equal(HttpStatusCode.OK, await PostAsync(test, "order.deleted", """{ "id": 1001 }"""));
        Assert.Equal(HttpStatusCode.OK, await PostAsync(test, "order.updated", Order(modified: "2026-11-05T10:30:00", total: "1.00")));

        var details = await ShopOrderAsync(test);
        Assert.Equal("cancelled", details.GetProperty("fulfillmentStatus").GetString());
        Assert.Equal("1000.00", details.GetProperty("value").GetString());
        Assert.NotEqual(JsonValueKind.Null, details.GetProperty("shopDeletedAtUtc").ValueKind);
        Assert.Contains("deletedInShop", details.GetProperty("shopWarnings").EnumerateArray().Select(w => w.GetString()));
    }

    [Fact]
    public async Task Deletion_of_unknown_order_is_remembered()
    {
        await using var test = await CreateAsync(sqlServer);

        await PostAsync(test, "order.deleted", """{ "id": 2002 }""");
        await PostAsync(test, "order.created", Order(id: 2002, modified: "2026-11-05T10:00:00"));

        var details = await ShopOrderAsync(test, "WC-2002");
        Assert.Equal("cancelled", details.GetProperty("fulfillmentStatus").GetString());
        Assert.NotEqual(JsonValueKind.Null, details.GetProperty("shopDeletedAtUtc").ValueKind);
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.ShopDeletions"));
    }

    [Fact]
    public async Task Bad_signature_or_missing_secret_changes_nothing()
    {
        await using (var test = await CreateAsync(sqlServer))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync(test, "order.created", Order(), signature: "AAAA"));
            Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync(test, "order.created", Order(), sign: false));
            Assert.Equal(0, await CountAsync(test));
            Assert.DoesNotContain(test.Factory.Logs.GetSnapshot(), log => log.Message.Contains("Testowy", StringComparison.Ordinal));
        }

        await using var disabled = await CreateAsync(sqlServer, enabled: false);
        Assert.Equal(HttpStatusCode.NotFound, await PostAsync(disabled, "order.created", Order()));
        Assert.Equal(0, await CountAsync(disabled));
    }

    [Fact]
    public async Task Ping_without_signature_and_unreadable_body_are_accepted_without_changes()
    {
        await using var test = await CreateAsync(sqlServer);

        Assert.Equal(HttpStatusCode.OK, await PostAsync(test, string.Empty, "webhook_id=15", sign: false));
        Assert.Equal(HttpStatusCode.OK, await PostAsync(test, "order.created", """{ "id": 1001, "status": "processing" }"""));
        Assert.Equal(HttpStatusCode.OK, await PostAsync(test, "product.updated", """{ "id": 5 }"""));

        Assert.Equal(0, await CountAsync(test));
    }

    [Fact]
    public async Task Hidden_shop_statuses_are_filtered_by_default()
    {
        await using var test = await CreateAsync(sqlServer);
        await PostAsync(test, "order.created", Order(id: 1, status: "pending"));
        await PostAsync(test, "order.created", Order(id: 2, status: "processing"));

        var visible = (await test.GetJsonAsync("/api/orders")).GetProperty("items").EnumerateArray().ToList();
        var all = (await test.GetJsonAsync("/api/orders?includeHiddenShopStatuses=true")).GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(["WC-2"], visible.Select(i => i.GetProperty("number").GetString()));
        Assert.Equal("processing", visible[0].GetProperty("shopStatus").GetString());
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task Value_drop_below_sales_reductions_shows_excess()
    {
        await using var test = await CreateAsync(sqlServer);
        await PostAsync(test, "order.created", Order(total: "1000.00"));
        var id = (await ShopOrderAsync(test)).GetProperty("id").GetGuid();
        await OrderEntryRequests.AddCorrectionAsync(test, id, "500.00");

        await PostAsync(test, "order.updated", Order(modified: "2026-11-05T12:00:00", total: "300.00"));

        var details = await ShopOrderAsync(test);
        Assert.Equal("300.00", details.GetProperty("value").GetString());
        Assert.Equal("200.00", details.GetProperty("salesReductions").GetProperty("excess").GetString());
    }

    [Fact]
    public async Task Restored_after_deletion_reactivates_shop_side_without_changing_erp_status()
    {
        await using var test = await CreateAsync(sqlServer);
        await PostAsync(test, "order.created", Order());
        await PostAsync(test, "order.deleted", """{ "id": 1001 }""");
        test.Time.Advance(TimeSpan.FromMinutes(5));

        // Przywrócenie z kosza ma datę modyfikacji po odebraniu usunięcia.
        var restoredAt = test.Time.GetUtcNow().UtcDateTime.AddMinutes(1).ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(HttpStatusCode.OK, await PostAsync(test, "order.restored", Order(modified: restoredAt)));

        var details = await ShopOrderAsync(test);
        Assert.Equal(JsonValueKind.Null, details.GetProperty("shopDeletedAtUtc").ValueKind);
        Assert.Equal("cancelled", details.GetProperty("fulfillmentStatus").GetString());
        Assert.Equal(["activeInShop"], details.GetProperty("shopWarnings").EnumerateArray().Select(w => w.GetString()));
    }

    [Fact]
    public async Task Concurrent_created_and_deleted_keep_the_deletion()
    {
        await using var test = await CreateAsync(sqlServer);

        await Task.WhenAll(
            PostAsync(test, "order.created", Order(id: 3003)),
            PostAsync(test, "order.deleted", """{ "id": 3003 }"""));

        var details = await ShopOrderAsync(test, "WC-3003");
        Assert.Equal("cancelled", details.GetProperty("fulfillmentStatus").GetString());
        Assert.NotEqual(JsonValueKind.Null, details.GetProperty("shopDeletedAtUtc").ValueKind);
    }

    [Fact]
    public async Task User_edit_with_version_from_before_sync_gets_conflict()
    {
        await using var test = await CreateAsync(sqlServer);
        await PostAsync(test, "order.created", Order());
        var before = await ShopOrderAsync(test);

        await PostAsync(test, "order.updated", Order(modified: "2026-11-05T12:00:00", total: "900.00"));
        using var response = await test.SendAsync(
            HttpMethod.Post,
            $"/api/orders/{before.GetProperty("id").GetGuid()}/cancel",
            new { version = before.GetProperty("version").GetString() });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Value_above_erp_range_is_unreadable_and_accepted()
    {
        await using var test = await CreateAsync(sqlServer);

        Assert.Equal(HttpStatusCode.OK, await PostAsync(test, "order.created", Order(total: "99999999999999999.00")));

        Assert.Equal(0, await CountAsync(test));
    }

    [Fact]
    public async Task Refund_in_shop_is_information_only()
    {
        await using var test = await CreateAsync(sqlServer);
        await PostAsync(test, "order.created", Order());

        await PostAsync(test, "order.updated", Order(status: "refunded", modified: "2026-11-05T11:00:00", refunds: """[{ "id": 1, "total": "-200.00" }]"""));

        var details = await ShopOrderAsync(test);
        Assert.Equal("200.00", details.GetProperty("shopRefundedAmount").GetString());
        Assert.Equal("inProgress", details.GetProperty("fulfillmentStatus").GetString());
        Assert.Equal(["refundedInShop"], details.GetProperty("shopWarnings").EnumerateArray().Select(w => w.GetString()));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Refunds"));
    }

    [Fact]
    public async Task Deliveries_after_anonymization_do_not_restore_customer_data()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer, settings: AnonymizationTestSupport.Retention(extra: Enabled));
        await PostAsync(test, "order.created", Order());
        var id = (await ShopOrderAsync(test)).GetProperty("id").GetGuid();
        await AnonymizationTestSupport.MoveClockAndLoginAsync(test, new DateTimeOffset(2026, 12, 10, 12, 0, 0, TimeSpan.Zero));
        await AnonymizationTestSupport.AnonymizeAsync(test, id);
        var syncAudit = await SyncAuditCountAsync(test);

        Assert.Equal(HttpStatusCode.OK, await PostAsync(test, "order.updated", Order(status: "completed", modified: "2026-12-10T10:00:00", total: "900.00")));
        var updated = await ShopOrderAsync(test);
        Assert.Equal(AnonymizationTestSupport.Marker, updated.GetProperty("customer").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("customer").GetProperty("email").ValueKind);
        Assert.Equal("900.00", updated.GetProperty("value").GetString());
        Assert.Equal("completed", updated.GetProperty("shopStatus").GetString());
        Assert.Equal("anonymized", updated.GetProperty("anonymization").GetProperty("status").GetString());
        Assert.Equal(syncAudit + 1, await SyncAuditCountAsync(test));
        var syncEntry = (await AnonymizationTestSupport.AuditAsync(test, id)).Last(row => row.AuthorKind == "WooCommerceSync");
        Assert.DoesNotContain(AnonymizationTestSupport.Changes(syncEntry.Changes), change => change.Field.StartsWith("customer", StringComparison.Ordinal));

        // Dostawa różna tylko danymi klienta: brak zmian pól, brak wpisu audytu.
        Assert.Equal(HttpStatusCode.OK, await PostAsync(test, "order.updated", Order(status: "completed", modified: "2026-12-10T10:30:00", total: "900.00")));
        Assert.Equal(syncAudit + 1, await SyncAuditCountAsync(test));

        Assert.Equal(HttpStatusCode.OK, await PostAsync(test, "order.deleted", """{ "id": 1001 }"""));
        test.Time.Advance(TimeSpan.FromMinutes(5));
        var restoredAt = test.Time.GetUtcNow().UtcDateTime.AddMinutes(1).ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(HttpStatusCode.OK, await PostAsync(test, "order.restored", Order(modified: restoredAt, total: "900.00")));

        var restored = await ShopOrderAsync(test);
        Assert.Equal(AnonymizationTestSupport.Marker, restored.GetProperty("customer").GetProperty("name").GetString());
        Assert.Equal(0, await AnonymizationTestSupport.OccurrencesAsync(test, "Testowy"));
        Assert.Equal(0, await AnonymizationTestSupport.OccurrencesAsync(test, "jan.testowy"));
    }

    [Fact]
    public async Task Delivery_saved_before_anonymization_makes_a_stale_request_a_conflict()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer, settings: AnonymizationTestSupport.Retention(extra: Enabled));
        await PostAsync(test, "order.created", Order());
        var id = (await ShopOrderAsync(test)).GetProperty("id").GetGuid();
        await AnonymizationTestSupport.MoveClockAndLoginAsync(test, new DateTimeOffset(2026, 12, 10, 12, 0, 0, TimeSpan.Zero));
        var version = await AnonymizationTestSupport.VersionAsync(test, id);

        Assert.Equal(HttpStatusCode.OK, await PostAsync(test, "order.updated", Order(modified: "2026-12-10T10:00:00", total: "950.00")));
        using var stale = await AnonymizationTestSupport.SendAnonymizeAsync(test, id, version);

        await OrderEntryRequests.AssertProblemAsync(stale, HttpStatusCode.Conflict, StoneFlow.Orders.Api.OrderProblems.ConflictCode);
        Assert.Equal("Jan Testowy", (await ShopOrderAsync(test)).GetProperty("customer").GetProperty("name").GetString());
    }
}
