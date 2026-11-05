using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using StoneFlow.Access.Api;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Orders.Api;
using static StoneFlow.Api.IntegrationTests.Orders.OrderEntryRequests;

namespace StoneFlow.Api.IntegrationTests.Orders;

/// <summary>
/// SF-10: korekty sprzedaży i limit obniżeń sprzedaży przez API na prawdziwej bazie — korekty,
/// zwroty sprzedaży, ręczne obniżenie wartości, nadwyżka po zmianie przez sklep, wyścigi pod
/// blokadą zamówienia, idempotencja, audyt i ochrona dostępu. Dane są fikcyjne.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SalesCorrectionsApiTests(SqlServerFixture sqlServer)
{
    [Fact]
    public async Task Correction_does_not_change_paid_or_state()
    {
        // PLAT-07: korekta 500 do zamówienia 1000 z zaliczką 300.
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        await AddPaymentAsync(test, order.Id, "300.00");
        var before = await test.GetJsonAsync($"/api/orders/{order.Id}");

        var correction = await AddCorrectionAsync(test, order.Id, "500.00", date: "2026-10-01");

        var details = await test.GetJsonAsync($"/api/orders/{order.Id}");
        Assert.Equal(("300.00", "partiallyPaid", "700.00", null, null), Summary(details.GetProperty("payment")));
        foreach (var property in new[] { "value", "fulfillmentStatus", "version", "updatedAtUtc" })
        {
            Assert.Equal(before.GetProperty(property).GetString(), details.GetProperty(property).GetString());
        }

        var entry = Assert.Single(details.GetProperty("salesCorrections").EnumerateArray());
        Assert.Equal(correction.Id, entry.GetProperty("id").GetGuid());
        Assert.Equal("500.00", entry.GetProperty("amount").GetString());
        Assert.Equal("2026-10-01", entry.GetProperty("date").GetString());
        Assert.Equal("Oddany kamień testowy", entry.GetProperty("reason").GetString());
        Assert.Equal(correction.Version, entry.GetProperty("version").GetString());
        Assert.EndsWith("Z", entry.GetProperty("createdAtUtc").GetString(), StringComparison.Ordinal);
        Assert.False(entry.TryGetProperty("method", out _));
        Assert.Equal(("0.00", "500.00", "500.00", null), await ReductionsAsync(test, order.Id));
        Assert.Equal(0, details.GetProperty("refunds").GetArrayLength());
        var item = (await test.GetJsonAsync("/api/orders")).GetProperty("items")[0];
        Assert.Equal(("300.00", "partiallyPaid"), (item.GetProperty("paid").GetString(), item.GetProperty("paymentState").GetString()));
    }

    [Fact]
    public async Task Limit_on_correction_add_and_edit()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        var first = await AddCorrectionAsync(test, order.Id, "600.00");

        using var tooMuch = await SendCorrectionAsync(test, order.Id, Correction("400.01"));
        var second = await AddCorrectionAsync(test, order.Id, "400.00");
        using var nothingLeft = await SendCorrectionAsync(test, order.Id, Correction("0.01"));
        using var raise = await SendCorrectionEditAsync(test, order.Id, first.Id, Correction("600.01", version: first.Version));
        var lowered = await EditCorrectionAsync(test, order.Id, first.Id, first.Version, "100.00");
        var raisedToLimit = await EditCorrectionAsync(test, order.Id, first.Id, lowered, "600.00");

        await AssertLimitAsync(tooMuch, available: "400.00");
        await AssertLimitAsync(nothingLeft, available: "0.00");
        await AssertLimitAsync(raise, available: "600.00");
        Assert.NotEqual(lowered, raisedToLimit);
        Assert.Equal(("0.00", "1000.00", "1000.00", null), await ReductionsAsync(test, order.Id));
        Assert.Equal(2, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.SalesCorrections"));
        await DeleteCorrectionAsync(test, order.Id, second.Id, second.Version);
        Assert.Equal(("0.00", "600.00", "600.00", null), await ReductionsAsync(test, order.Id));
    }

    [Fact]
    public async Task Limit_on_sales_return_and_money_return_is_not_limited()
    {
        // PLAT-05: zwrot sprzedaży 1200 przy wartości 1000 i wpłatach 1200.
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        await AddPaymentAsync(test, order.Id, "1200.00");

        using var aboveValue = await SendRefundAsync(test, order.Id, Refund("1200.00", kind: "salesReturn"));
        // Ta sama zmiana narusza obie reguły: najpierw „zwrot ≤ Zapłacono”.
        using var abovePaid = await SendRefundAsync(test, order.Id, Refund("1300.00", kind: "salesReturn"));
        await AddRefundAsync(test, order.Id, "1200.00", kind: "moneyReturn");

        await AssertLimitAsync(aboveValue, available: "1000.00");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, abovePaid.StatusCode);
        Assert.Equal(OrderProblems.RefundExceedsPaidCode, (await Json(abovePaid)).GetProperty("code").GetString());
        Assert.Equal(("0.00", "0.00", "0.00", null), await ReductionsAsync(test, order.Id));
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Refunds"));
    }

    [Fact]
    public async Task Refund_kind_change_respects_limit()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        await AddPaymentAsync(test, order.Id, "1000.00");
        await AddCorrectionAsync(test, order.Id, "500.00");
        var refund = await AddRefundAsync(test, order.Id, "600.00", kind: "moneyReturn");

        using var toSalesReturn = await test.SendAsync(
            HttpMethod.Put, $"/api/orders/{order.Id}/refunds/{refund.Id}", Refund("600.00", kind: "salesReturn", version: refund.Version));
        var withinLimit = await EditRefundAsync(test, order.Id, refund.Id, refund.Version, "500.00", kind: "salesReturn");
        using var raiseSalesReturn = await test.SendAsync(
            HttpMethod.Put, $"/api/orders/{order.Id}/refunds/{refund.Id}", Refund("500.01", kind: "salesReturn", version: withinLimit));
        // Zmiana na zwrot pieniędzy obniża obniżenia, więc przechodzi.
        await EditRefundAsync(test, order.Id, refund.Id, withinLimit, "600.00", kind: "moneyReturn");

        await AssertLimitAsync(toSalesReturn, available: "500.00");
        await AssertLimitAsync(raiseSalesReturn, available: "500.00");
        Assert.Equal(("0.00", "500.00", "500.00", null), await ReductionsAsync(test, order.Id));
    }

    [Fact]
    public async Task Edit_order_value_below_reductions_is_rejected()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        await AddPaymentAsync(test, order.Id, "1000.00");
        await AddCorrectionAsync(test, order.Id, "300.00");
        await AddRefundAsync(test, order.Id, "200.00", kind: "salesReturn");
        await AddRefundAsync(test, order.Id, "700.00", kind: "moneyReturn");

        using var tooLow = await test.SendAsync(HttpMethod.Put, $"/api/orders/{order.Id}", Order("400.00", version: order.Version));
        var atLimit = await test.EditAsync(order.Id, Order("500.00", version: order.Version));
        var raised = await test.EditAsync(order.Id, Order("800.00", version: atLimit));
        // Bez zmiany wartości limit nie jest sprawdzany.
        await test.EditAsync(order.Id, Order("800.00", comment: "Uwaga", version: raised));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooLow.StatusCode);
        var problem = await Json(tooLow);
        Assert.Equal(OrderProblems.SalesReductionLimitExceededCode, problem.GetProperty("code").GetString());
        Assert.Equal("500.00", problem.GetProperty("minimumValue").GetString());
        Assert.False(problem.TryGetProperty("available", out _));
        var audit = await test.AuditRowsAsync(order.Id);
        Assert.Equal(4, audit.Count);
        Assert.Equal("800.00", (await test.GetJsonAsync($"/api/orders/{order.Id}")).GetProperty("value").GetString());
    }

    [Fact]
    public async Task Excess_warning_from_seeded_state_and_decreases_allowed_in_excess()
    {
        // Stan jak po synchronizacji sklepu (SF-12): wartość obniżona do 300 przy obniżeniach 500.
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        await AddPaymentAsync(test, order.Id, "500.00");
        var correction = await AddCorrectionAsync(test, order.Id, "400.00");
        await AddRefundAsync(test, order.Id, "100.00", kind: "salesReturn");
        await test.ExecuteSqlAsync($"UPDATE orders.Orders SET Value = 300.00 WHERE Id = '{order.Id}';");

        var seeded = await ReductionsAsync(test, order.Id);
        var reasonOnly = await EditCorrectionAsync(test, order.Id, correction.Id, correction.Version, "400.00", reason: "Inny powód");
        using var raise = await SendCorrectionEditAsync(test, order.Id, correction.Id, Correction("400.01", version: reasonOnly));
        using var newCorrection = await SendCorrectionAsync(test, order.Id, Correction("0.01"));
        using var newSalesReturn = await SendRefundAsync(test, order.Id, Refund("0.01", kind: "salesReturn"));
        var lowered = await EditCorrectionAsync(test, order.Id, correction.Id, reasonOnly, "250.00");
        var afterLowering = await ReductionsAsync(test, order.Id);
        await DeleteCorrectionAsync(test, order.Id, correction.Id, lowered);

        Assert.Equal(("100.00", "400.00", "500.00", "200.00"), seeded);
        await AssertLimitAsync(raise, available: "200.00");
        await AssertLimitAsync(newCorrection, available: "0.00");
        await AssertLimitAsync(newSalesReturn, available: "0.00");
        Assert.Equal(("100.00", "250.00", "350.00", "50.00"), afterLowering);
        Assert.Equal(("100.00", "0.00", "100.00", null), await ReductionsAsync(test, order.Id));
    }

    [Fact]
    public async Task Edit_order_value_in_excess_rejects_decrease_and_allows_partial_increase()
    {
        // Nadwyżka jak po synchronizacji: wartość 300 przy obniżeniach 500.
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        await AddCorrectionAsync(test, order.Id, "500.00");
        await test.ExecuteSqlAsync($"UPDATE orders.Orders SET Value = 300.00 WHERE Id = '{order.Id}';");
        var seededVersion = (await test.GetJsonAsync($"/api/orders/{order.Id}")).GetProperty("version").GetString()!;

        using var decrease = await test.SendAsync(HttpMethod.Put, $"/api/orders/{order.Id}", Order("200.00", version: seededVersion));
        var increased = await test.EditAsync(order.Id, Order("400.00", version: seededVersion));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, decrease.StatusCode);
        var problem = await Json(decrease);
        Assert.Equal(OrderProblems.SalesReductionLimitExceededCode, problem.GetProperty("code").GetString());
        Assert.Equal("500.00", problem.GetProperty("minimumValue").GetString());
        var details = await test.GetJsonAsync($"/api/orders/{order.Id}");
        Assert.Equal(("400.00", increased), (details.GetProperty("value").GetString(), details.GetProperty("version").GetString()));
        Assert.Equal(("0.00", "500.00", "500.00", "100.00"), await ReductionsAsync(test, order.Id));
    }

    [Fact]
    public async Task Refund_edit_violating_both_rules_reports_refund_exceeds_paid_first()
    {
        // Plan SF-10, N3: przy poprawie zwrotu kolejność jak przy dodaniu — najpierw Zapłacono.
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        await AddPaymentAsync(test, order.Id, "1000.00");
        await AddCorrectionAsync(test, order.Id, "500.00");
        var refund = await AddRefundAsync(test, order.Id, "600.00", kind: "moneyReturn");

        // Zwrot sprzedaży 1100: Zapłacono byłoby −100, a obniżenia 1600 > 1000.
        using var both = await test.SendAsync(
            HttpMethod.Put, $"/api/orders/{order.Id}/refunds/{refund.Id}", Refund("1100.00", kind: "salesReturn", version: refund.Version));

        await AssertProblemAsync(both, HttpStatusCode.UnprocessableEntity, OrderProblems.RefundExceedsPaidCode);
        Assert.Equal("1000.00", (await Json(both)).GetProperty("available").GetString());
        var stored = (await test.GetJsonAsync($"/api/orders/{order.Id}")).GetProperty("refunds")[0];
        Assert.Equal(("600.00", "moneyReturn", refund.Version), (stored.GetProperty("amount").GetString(), stored.GetProperty("kind").GetString(), stored.GetProperty("version").GetString()));
    }

    [Fact]
    public async Task Cancelled_and_shop_orders_accept_corrections_with_the_same_limit()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        await test.StatusActionAsync(order.Id, "cancel", order.Version);
        var shop = await test.SeedShopOrderAsync();

        await AddCorrectionAsync(test, order.Id, "1000.00");
        using var cancelledAbove = await SendCorrectionAsync(test, order.Id, Correction("0.01"));
        await AddCorrectionAsync(test, shop, "250.00");
        using var shopAbove = await SendCorrectionAsync(test, shop, Correction("0.01"));

        await AssertLimitAsync(cancelledAbove, available: "0.00");
        await AssertLimitAsync(shopAbove, available: "0.00");
        Assert.Equal(("0.00", null, null, null, null), Summary((await test.GetJsonAsync($"/api/orders/{order.Id}")).GetProperty("payment")));
    }

    [Fact]
    public async Task Parallel_correction_and_sales_return_respect_limit()
    {
        // Korekta 600 i zwrot sprzedaży 600 przy wartości 1000 i wpłatach 1000. Dekorator czeka przed
        // zapisem, aż drugi wpis też dojdzie do zapisu (najwyżej 2 s): bez blokady oba przeszłyby limit.
        var hooks = new EntryStoreHooks();
        await using var test = await OrdersTest.CreateAsync(sqlServer, configureServices: hooks.Register);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        await AddPaymentAsync(test, order.Id, "1000.00");
        var barrier = Barrier();
        hooks.BeforeAdd = _ => barrier();

        var responses = await Task.WhenAll(
            SendCorrectionAsync(test, order.Id, Correction("600.00")),
            SendRefundAsync(test, order.Id, Refund("600.00", kind: "salesReturn")));
        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            await AssertLimitAsync(Assert.Single(responses, r => r.StatusCode == HttpStatusCode.UnprocessableEntity), available: "400.00");
            Assert.Equal("600.00", (await ReductionsAsync(test, order.Id)).Total);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Parallel_value_edit_and_correction_respect_limit()
    {
        // Obniżenie wartości 1000 → 400 i korekta 500. Oba zapisy czekają przed zapisem na siebie
        // (najwyżej 2 s): bez blokady w EditOrder i w korekcie oba przeszłyby, a obniżenia (500)
        // przekroczyłyby wartość (400). Z blokadą dokładnie jedna zmiana przechodzi.
        var orderHooks = new StoreHooks();
        var entryHooks = new EntryStoreHooks();
        await using var test = await OrdersTest.CreateAsync(sqlServer, hooks: orderHooks, configureServices: entryHooks.Register);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        var barrier = Barrier();
        orderHooks.BeforeSave = _ => barrier();
        entryHooks.BeforeAdd = _ => barrier();

        var responses = await Task.WhenAll(
            test.SendAsync(HttpMethod.Put, $"/api/orders/{order.Id}", Order("400.00", version: order.Version)),
            SendCorrectionAsync(test, order.Id, Correction("500.00")));
        try
        {
            Assert.Single(responses, r => r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created);
            var rejected = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.UnprocessableEntity);
            Assert.Equal(OrderProblems.SalesReductionLimitExceededCode, (await Json(rejected)).GetProperty("code").GetString());
            var value = decimal.Parse((await test.GetJsonAsync($"/api/orders/{order.Id}")).GetProperty("value").GetString()!, CultureInfo.InvariantCulture);
            var total = decimal.Parse((await ReductionsAsync(test, order.Id)).Total!, CultureInfo.InvariantCulture);
            Assert.True(total <= value, $"Obniżenia {total} przekraczają wartość {value}.");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Parallel_adds_with_same_key_create_one_correction_and_key_is_bound_to_order()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        var other = await test.CreateOrderAsync(Order("1000.00"));
        var key = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => SendCorrectionAsync(test, order.Id, Correction("100.00"), key)));
        try
        {
            Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, r.StatusCode.ToString()));
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.SalesCorrections"));
            var id = (await Json(responses[0])).GetProperty("id").GetGuid();

            using var otherOrder = await SendCorrectionAsync(test, other.Id, Correction("100.00"), key);
            Assert.Equal(HttpStatusCode.Conflict, otherOrder.StatusCode);
            var problem = await Json(otherOrder);
            Assert.Equal(OrderProblems.IdempotencyKeyReusedCode, problem.GetProperty("code").GetString());
            Assert.Equal(id, problem.GetProperty("salesCorrectionId").GetGuid());
            Assert.Single(await EntryAuditRowsAsync(test, order.Id));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Repeated_correction_that_reached_the_limit_returns_200()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        var key = Guid.NewGuid();

        var created = await AddCorrectionAsync(test, order.Id, "1000.00", key: key);
        using var repeated = await SendCorrectionAsync(test, order.Id, Correction(" 1000 "), key);

        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(created.Id, (await Json(repeated)).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Correction_lifecycle_writes_audit()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        var correction = await AddCorrectionAsync(test, order.Id, "500.00", date: "2026-11-02");
        var unchanged = await EditCorrectionAsync(test, order.Id, correction.Id, correction.Version, " 500 ", date: "2026-11-02");
        var edited = await EditCorrectionAsync(test, order.Id, correction.Id, correction.Version, "450.00", date: "2026-11-03", reason: "Część towaru");
        await DeleteCorrectionAsync(test, order.Id, correction.Id, edited);

        Assert.Equal(correction.Version, unchanged);
        var rows = await EntryAuditRowsAsync(test, order.Id);
        Assert.Equal(["Created", "Updated", "Deleted"], rows.Select(r => r.Operation));
        Assert.All(rows, r => Assert.Equal(("SalesCorrection", correction.Id, $"Korekta sprzedaży do {order.Number}"), (r.Type, r.Id, r.Label)));
        Assert.All(rows, r => Assert.Equal(("Order", order.Id, "Account"), (r.ParentType, r.ParentId, r.AuthorKind)));
        Assert.Equal([new DateTime(2026, 11, 2), new DateTime(2026, 11, 3), new DateTime(2026, 11, 3)], rows.Select(r => r.BusinessDate));
        Assert.Equal(
            [("amount", null, "500.00", false), ("date", null, "2026-11-02", false), ("reason", null, "Oddany kamień testowy", false)],
            Changes(rows[0]));
        Assert.Equal(
            [("amount", "500.00", "450.00", false), ("date", "2026-11-02", "2026-11-03", false), ("reason", "Oddany kamień testowy", "Część towaru", false)],
            Changes(rows[1]));
        Assert.Equal(
            [("amount", "450.00", null, false), ("date", "2026-11-03", null, false), ("reason", "Część towaru", null, false)],
            Changes(rows[2]));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.SalesCorrections"));
    }

    [Fact]
    public async Task Audit_failure_rolls_back_correction()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        var correction = await AddCorrectionAsync(test, order.Id, "500.00");
        await test.ExecuteSqlAsync("ALTER TABLE reporting.AuditEntries WITH NOCHECK ADD CONSTRAINT CK_Test_RejectAll CHECK (ObjectLabel = N'nigdy');");

        using var add = await SendCorrectionAsync(test, order.Id, Correction("10.00"));
        using var edit = await SendCorrectionEditAsync(test, order.Id, correction.Id, Correction("400.00", version: correction.Version));
        using var delete = await test.SendAsync(HttpMethod.Delete, $"/api/orders/{order.Id}/sales-corrections/{correction.Id}", new { version = correction.Version });
        using var value = await test.SendAsync(HttpMethod.Put, $"/api/orders/{order.Id}", Order("600.00", version: order.Version));

        foreach (var response in new[] { add, edit, delete, value })
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        var details = await test.GetJsonAsync($"/api/orders/{order.Id}");
        var entry = Assert.Single(details.GetProperty("salesCorrections").EnumerateArray());
        Assert.Equal(("500.00", correction.Version), (entry.GetProperty("amount").GetString(), entry.GetProperty("version").GetString()));
        Assert.Equal(("1000.00", order.Version), (details.GetProperty("value").GetString(), details.GetProperty("version").GetString()));
        Assert.Single(await EntryAuditRowsAsync(test, order.Id));
    }

    [Fact]
    public async Task Conflicts_404_and_validation()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        var other = await test.CreateOrderAsync(Order("1000.00"));
        var correction = await AddCorrectionAsync(test, order.Id, "300.00");
        var edited = await EditCorrectionAsync(test, order.Id, correction.Id, correction.Version, "350.00");

        using var staleEdit = await SendCorrectionEditAsync(test, order.Id, correction.Id, Correction("100.00", version: correction.Version));
        using var staleDelete = await test.SendAsync(
            HttpMethod.Delete, $"/api/orders/{order.Id}/sales-corrections/{correction.Id}", new { version = correction.Version });
        using var otherOrder = await SendCorrectionEditAsync(test, other.Id, correction.Id, Correction("100.00", version: edited));
        using var unknownOrder = await SendCorrectionAsync(test, Guid.NewGuid(), Correction("100.00"));
        using var withoutVersion = await test.SendAsync(HttpMethod.Delete, $"/api/orders/{order.Id}/sales-corrections/{correction.Id}");
        using var invalid = await SendCorrectionAsync(test, order.Id, new { amount = "-1", date = "2026-11-06", reason = " " });
        using var withoutKey = await test.SendAsync(HttpMethod.Post, $"/api/orders/{order.Id}/sales-corrections", Correction("1.00"));
        await DeleteCorrectionAsync(test, order.Id, correction.Id, edited);
        using var deletedAgain = await test.SendAsync(
            HttpMethod.Delete, $"/api/orders/{order.Id}/sales-corrections/{correction.Id}", new { version = edited });

        await AssertProblemAsync(staleEdit, HttpStatusCode.Conflict, OrderProblems.EntryConflictCode);
        await AssertProblemAsync(staleDelete, HttpStatusCode.Conflict, OrderProblems.EntryConflictCode);
        await AssertProblemAsync(otherOrder, HttpStatusCode.NotFound, OrderProblems.EntryNotFoundCode);
        await AssertProblemAsync(unknownOrder, HttpStatusCode.NotFound, OrderProblems.NotFoundCode);
        Assert.Equal(["version"], await ErrorKeysAsync(withoutVersion));
        Assert.Equal(["amount", "date", "reason"], await ErrorKeysAsync(invalid));
        await AssertProblemAsync(withoutKey, HttpStatusCode.BadRequest, OrderProblems.IdempotencyKeyRequiredCode);
        await AssertProblemAsync(deletedAgain, HttpStatusCode.NotFound, OrderProblems.EntryNotFoundCode);
    }

    [Fact]
    public async Task Correction_routes_require_session_and_xsrf()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        var correction = await AddCorrectionAsync(test, order.Id, "300.00");
        (HttpMethod Method, string Path, object Body)[] routes =
        [
            (HttpMethod.Post, $"/api/orders/{order.Id}/sales-corrections", Correction("1.00")),
            (HttpMethod.Put, $"/api/orders/{order.Id}/sales-corrections/{correction.Id}", Correction("2.00", version: correction.Version)),
            (HttpMethod.Delete, $"/api/orders/{order.Id}/sales-corrections/{correction.Id}", new { version = correction.Version }),
        ];
        using var anonymous = AuthClient.Create(test.Factory);
        (await anonymous.GetCsrfAsync()).Dispose();

        foreach (var (method, path, body) in routes)
        {
            using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body) };
            request.Headers.Add("X-XSRF-TOKEN", anonymous.XsrfToken);
            request.Headers.Add(OrdersTest.IdempotencyKeyHeader, Guid.NewGuid().ToString());
            using var unauthorized = await anonymous.Http.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.True(unauthorized.StatusCode == HttpStatusCode.Unauthorized, $"{method} {path} → {unauthorized.StatusCode}");

            using var withoutXsrf = await test.SendAsync(method, path, body, Guid.NewGuid().ToString(), withCsrf: false);
            Assert.Equal(HttpStatusCode.BadRequest, withoutXsrf.StatusCode);
            Assert.Equal(AuthProblems.CsrfInvalidCode, (await Json(withoutXsrf)).GetProperty("code").GetString());
        }

        Assert.Equal(("0.00", "300.00", "300.00", null), await ReductionsAsync(test, order.Id));
        Assert.Single(await EntryAuditRowsAsync(test, order.Id));
    }

    private static async Task AssertLimitAsync(HttpResponseMessage response, string available)
    {
        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, OrderProblems.SalesReductionLimitExceededCode);
        var problem = await Json(response);
        Assert.Equal(available, problem.GetProperty("available").GetString());
        Assert.StartsWith("Suma zwrotów sprzedaży i korekt nie może przekroczyć wartości zamówienia.", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }
}
