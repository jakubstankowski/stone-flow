using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Api.IntegrationTests.Expenses;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Api.IntegrationTests.Orders;
using StoneFlow.Reporting.Api;
using StoneFlow.Shared.Application.Costs;
using StoneFlow.Shared.Application.Sales;

namespace StoneFlow.Api.IntegrationTests.Reporting;

/// <summary>
/// SF-19: raport sprzedaży i zestawienie składników na prawdziwej bazie z danymi z Orders i
/// Expenses — reguła statusów, zwroty, korekty, anulowanie, przełom uruchomienia, koszty i suma
/// listy wydatków, porównanie, bezpiecznik, walidacja i dostęp. Dane są fikcyjne.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SalesReportApiTests(SqlServerFixture sqlServer)
{
    private const string Sales = "/api/reports/sales";
    private const string Components = "/api/reports/sales/components";

    /// <summary>Zegar 10.12.2026 i data uruchomienia 1.09.2026: październik i listopad są w całości po uruchomieniu.</summary>
    private Task<OrdersTest> LateAsync() => OrdersTest.CreateAsync(
        sqlServer,
        new DateTimeOffset(2026, 12, 10, 12, 0, 0, TimeSpan.Zero),
        settings: new Dictionary<string, string?> { ["StoneFlow:LaunchDate"] = "2026-09-01" });

    // Domyślnie: zegar 5.11.2026, data uruchomienia 3.11.2026.
    private Task<OrdersTest> DefaultAsync() => OrdersTest.CreateAsync(sqlServer);

    [Fact]
    public async Task Cards_channels_and_components_agree_and_compare_with_the_previous_month()
    {
        await using var test = await LateAsync();
        var a = await test.CreateOrderAsync(OrderEntryRequests.Order("1000.00", orderDate: "2026-11-04", channel: "olx"));
        var b = await test.CreateOrderAsync(OrderEntryRequests.Order("500.00", orderDate: "2026-11-10", channel: "phone"));
        await test.CreateOrderAsync(OrderEntryRequests.Order("800.00", orderDate: "2026-10-15", channel: "acquaintance"));
        await OrderEntryRequests.AddPaymentAsync(test, a.Id, "1000.00", "2026-12-01"); // wpłata w innym miesiącu
        await AddRefundAsync(test, a.Id, "200.00", "salesReturn", "2026-11-20");
        await OrderEntryRequests.AddCorrectionAsync(test, b.Id, "100.00", "2026-11-25");
        await CreateExpenseAsync(test, ExpensesTest.Input(costDate: "2026-11-06", dueDate: null, grossAmount: "300.00", netAmount: null));

        var report = await test.GetJsonAsync($"{Sales}?period=previousMonth");

        var period = report.GetProperty("period");
        Assert.Equal("previousMonth", period.GetProperty("kind").GetString());
        Assert.Equal("2026-11-01", period.GetProperty("from").GetString());
        Assert.Equal("2026-11-30", period.GetProperty("to").GetString());
        Assert.Equal("day", period.GetProperty("granularity").GetString());
        Assert.False(period.GetProperty("inProgress").GetBoolean());
        var comparison = report.GetProperty("comparison");
        Assert.Equal("2026-10-01", comparison.GetProperty("from").GetString());
        Assert.Equal("2026-10-31", comparison.GetProperty("to").GetString());
        Assert.True(comparison.GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, comparison.GetProperty("reason").ValueKind);

        var kpis = report.GetProperty("kpis");
        AssertAmountKpi(kpis.GetProperty("sales"), "1200.00", "available", "800.00", "400.00", "50.0");
        AssertAmountKpi(kpis.GetProperty("costs"), "300.00", "noBasis", null, null, null);
        AssertAmountKpi(kpis.GetProperty("result"), "900.00", "available", "800.00", "100.00", "12.5");
        var orderCount = kpis.GetProperty("orderCount");
        Assert.Equal(2, orderCount.GetProperty("value").GetInt32());
        Assert.Equal(1, orderCount.GetProperty("comparison").GetProperty("base").GetInt32());
        Assert.Equal(1, orderCount.GetProperty("comparison").GetProperty("difference").GetInt32());
        Assert.Equal("100.0", orderCount.GetProperty("comparison").GetProperty("percent").GetString());

        Assert.Equal(
            [("shop", "0.00"), ("olx", "800.00"), ("acquaintance", "0.00"), ("phone", "400.00"), ("other", "0.00")],
            report.GetProperty("channels").EnumerateArray().Select(c => (c.GetProperty("channel").GetString(), c.GetProperty("amount").GetString())));
        var series = report.GetProperty("series").EnumerateArray().ToList();
        Assert.Equal(30, series.Count);
        Assert.Equal(1200.00m, series.Sum(p => Amount(p.GetProperty("sales"))));
        Assert.Equal(300.00m, series.Sum(p => Amount(p.GetProperty("costs"))));
        Assert.Equal(900.00m, series.Sum(p => Amount(p.GetProperty("result"))));
        Assert.Equal("2026-11-20", series[19].GetProperty("from").GetString());
        Assert.Equal("-200.00", series[19].GetProperty("sales").GetString());

        // Suma zestawienia = karta niezależnie od strony i kierunku.
        foreach (var query in new[] { "", "&pageSize=1", "&pageSize=1&page=3&direction=asc", "&page=9" })
        {
            var page = await test.GetJsonAsync($"{Components}?period=previousMonth{query}");
            Assert.Equal("1200.00", page.GetProperty("sum").GetString());
            Assert.Equal(4, page.GetProperty("total").GetInt32());
            Assert.Equal("2026-11-01", page.GetProperty("period").GetProperty("from").GetString());
        }

        var all = (await test.GetJsonAsync($"{Components}?period=previousMonth")).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["salesCorrection", "salesReturn", "order", "order"], all.Select(i => i.GetProperty("kind").GetString()));
        Assert.Equal("-100.00", all[0].GetProperty("amount").GetString());
        Assert.Equal(b.Id, all[0].GetProperty("orderId").GetGuid());
        Assert.Equal(b.Number, all[0].GetProperty("orderNumber").GetString());
        Assert.Equal("phone", all[0].GetProperty("channel").GetString());
        Assert.Equal("2026-11-25", all[0].GetProperty("date").GetString());

        var orders = await test.GetJsonAsync($"{Components}?period=previousMonth&kind=order");
        Assert.Equal(2, orders.GetProperty("total").GetInt32());
        Assert.Equal("800.00", (await test.GetJsonAsync($"{Components}?period=previousMonth&channel=olx")).GetProperty("sum").GetString());
        Assert.Equal("-200.00", (await test.GetJsonAsync($"{Components}?period=previousMonth&kind=salesReturn")).GetProperty("sum").GetString());

        // Wpłata z grudnia nie tworzy sprzedaży w grudniu.
        var december = await test.GetJsonAsync(Sales);
        Assert.Equal("0.00", december.GetProperty("kpis").GetProperty("sales").GetProperty("value").GetString());
        Assert.True(december.GetProperty("period").GetProperty("inProgress").GetBoolean());
    }

    [Fact]
    public async Task Correction_to_an_october_order_lowers_november_and_leaves_october()
    {
        await using var test = await LateAsync();
        var order = await test.CreateOrderAsync(OrderEntryRequests.Order("400.00", orderDate: "2026-10-15"));
        var correction = await OrderEntryRequests.AddCorrectionAsync(test, order.Id, "100.00", "2026-11-12");

        Assert.Equal("400.00", await SalesValueAsync(test, "period=custom&from=2026-10-01&to=2026-10-31"));
        Assert.Equal("-100.00", await SalesValueAsync(test, "period=previousMonth"));
        var item = Assert.Single((await test.GetJsonAsync($"{Components}?period=previousMonth")).GetProperty("items").EnumerateArray());
        Assert.Equal(correction.Id, item.GetProperty("id").GetGuid());
        Assert.Equal(order.Id, item.GetProperty("orderId").GetGuid());
    }

    [Fact]
    public async Task Shop_status_rule_is_applied_once_in_the_domain_also_for_null_and_unknown_statuses()
    {
        await using var test = await LateAsync();
        await test.CreateOrderAsync(OrderEntryRequests.Order("100.00", orderDate: "2026-11-02")); // ręczne: ShopStatus NULL
        await InsertShopOrderAsync(test, "custom-status", "10.00", 1);
        await InsertShopOrderAsync(test, "on-hold", "20.00", 2);
        await InsertShopOrderAsync(test, "refunded", "40.00", 3);
        await InsertShopOrderAsync(test, "cancelled", "5.00", 4); // anulowane w sklepie, aktywne w ERP
        var pending = await InsertShopOrderAsync(test, "pending", "1000.00", 5);
        await InsertShopOrderAsync(test, "failed", "2000.00", 6);
        await InsertShopOrderAsync(test, "checkout-draft", "3000.00", 7);
        await test.ExecuteSqlAsync(
            "INSERT INTO orders.SalesCorrections (Id, OrderId, Amount, CorrectedOn, Reason, CreatedAtUtc, UpdatedAtUtc) "
            + $"VALUES (NEWID(), '{pending}', 50.00, '2026-11-15', N'Korekta testowa', SYSUTCDATETIME(), SYSUTCDATETIME());");
        var cancelled = await test.CreateOrderAsync(OrderEntryRequests.Order("5000.00", orderDate: "2026-11-03"));
        await OrderEntryRequests.AddCorrectionAsync(test, cancelled.Id, "70.00", "2026-11-04");
        await test.StatusActionAsync(cancelled.Id, "cancel", await VersionAsync(test, cancelled.Id));

        var before = (await test.GetJsonAsync($"{Sales}?period=previousMonth")).GetProperty("kpis");
        Assert.Equal("175.00", before.GetProperty("sales").GetProperty("value").GetString());
        Assert.Equal(5, before.GetProperty("orderCount").GetProperty("value").GetInt32());
        Assert.Equal(5, (await test.GetJsonAsync($"{Components}?period=previousMonth")).GetProperty("total").GetInt32());

        await test.ExecuteSqlAsync($"UPDATE orders.Orders SET ShopStatus = N'processing' WHERE Id = '{pending}';");

        var after = (await test.GetJsonAsync($"{Sales}?period=previousMonth")).GetProperty("kpis");
        Assert.Equal("1125.00", after.GetProperty("sales").GetProperty("value").GetString());
        Assert.Equal(6, after.GetProperty("orderCount").GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task Money_return_does_not_lower_sales_and_is_not_a_component()
    {
        await using var test = await DefaultAsync();
        var order = await test.CreateOrderAsync(OrderEntryRequests.Order("1000.00", orderDate: "2026-11-04"));
        await OrderEntryRequests.AddPaymentAsync(test, order.Id, "1200.00", "2026-11-05");
        await AddRefundAsync(test, order.Id, "200.00", "moneyReturn", "2026-11-05");

        Assert.Equal("1000.00", await SalesValueAsync(test, "period=currentMonth"));
        var page = await test.GetJsonAsync(Components);
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        Assert.Equal("order", Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Changing_the_order_channel_moves_its_sales_return()
    {
        await using var test = await DefaultAsync();
        var order = await test.CreateOrderAsync(OrderEntryRequests.Order("1000.00", orderDate: "2026-11-04", channel: "olx"));
        await OrderEntryRequests.AddPaymentAsync(test, order.Id, "1000.00", "2026-11-04");
        await AddRefundAsync(test, order.Id, "300.00", "salesReturn", "2026-11-05");

        await test.EditAsync(order.Id, OrderEntryRequests.Order("1000.00", version: await VersionAsync(test, order.Id), orderDate: "2026-11-04", channel: "phone"));

        var channels = (await test.GetJsonAsync(Sales)).GetProperty("channels").EnumerateArray()
            .ToDictionary(c => c.GetProperty("channel").GetString()!, c => c.GetProperty("amount").GetString());
        Assert.Equal("700.00", channels["phone"]);
        Assert.Equal("0.00", channels["olx"]);
        Assert.Equal("700.00", (await test.GetJsonAsync($"{Components}?channel=phone")).GetProperty("sum").GetString());
    }

    [Fact]
    public async Task Cancelling_removes_the_order_and_its_return_from_both_months_and_restoring_brings_them_back()
    {
        await using var test = await LateAsync();
        var order = await test.CreateOrderAsync(OrderEntryRequests.Order("1000.00", orderDate: "2026-10-15"));
        await OrderEntryRequests.AddPaymentAsync(test, order.Id, "1000.00", "2026-10-20");
        await AddRefundAsync(test, order.Id, "300.00", "salesReturn", "2026-11-05");
        const string october = "period=custom&from=2026-10-01&to=2026-10-31";

        Assert.Equal("1000.00", await SalesValueAsync(test, october));
        Assert.Equal("-300.00", await SalesValueAsync(test, "period=previousMonth"));

        var cancelled = await test.StatusActionAsync(order.Id, "cancel", await VersionAsync(test, order.Id));
        Assert.Equal("0.00", await SalesValueAsync(test, october));
        Assert.Equal("0.00", await SalesValueAsync(test, "period=previousMonth"));
        Assert.Equal(0, (await test.GetJsonAsync($"{Sales}?{october}")).GetProperty("kpis").GetProperty("orderCount").GetProperty("value").GetInt32());
        Assert.Equal(0, (await test.GetJsonAsync($"{Components}?period=previousMonth")).GetProperty("total").GetInt32());

        await test.StatusActionAsync(order.Id, "restore", cancelled);
        Assert.Equal("1000.00", await SalesValueAsync(test, october));
        Assert.Equal("-300.00", await SalesValueAsync(test, "period=previousMonth"));
    }

    [Fact]
    public async Task Order_from_before_launch_and_its_later_return_do_not_count()
    {
        await using var test = await DefaultAsync();
        var order = await test.CreateOrderAsync(OrderEntryRequests.Order("1000.00")); // 2026-10-28
        await OrderEntryRequests.AddPaymentAsync(test, order.Id, "1000.00", "2026-11-04");
        await AddRefundAsync(test, order.Id, "300.00", "salesReturn", "2026-11-04");

        var report = await test.GetJsonAsync(Sales);

        Assert.Equal("2026-11-03", report.GetProperty("period").GetProperty("effectiveFrom").GetString());
        Assert.Equal("2026-11-03", report.GetProperty("period").GetProperty("launchDate").GetString());
        Assert.False(report.GetProperty("comparison").GetProperty("available").GetBoolean());
        Assert.Equal("beforeLaunch", report.GetProperty("comparison").GetProperty("reason").GetString());
        var sales = report.GetProperty("kpis").GetProperty("sales");
        Assert.Equal("0.00", sales.GetProperty("value").GetString());
        Assert.Equal("noBasis", sales.GetProperty("comparison").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, sales.GetProperty("comparison").GetProperty("percent").ValueKind);
        Assert.Equal(28, report.GetProperty("series").GetArrayLength()); // 03.11–30.11
        var page = await test.GetJsonAsync(Components);
        Assert.Equal(0, page.GetProperty("total").GetInt32());
        Assert.Equal("0.00", page.GetProperty("sum").GetString());
    }

    [Fact]
    public async Task Period_wholly_before_launch_has_no_data_instead_of_zeros()
    {
        await using var test = await DefaultAsync();
        await test.CreateOrderAsync(OrderEntryRequests.Order("1000.00"));
        const string october = "period=custom&from=2026-10-01&to=2026-10-31";

        var report = await test.GetJsonAsync($"{Sales}?{october}");
        var page = await test.GetJsonAsync($"{Components}?{october}");

        Assert.True(report.GetProperty("period").GetProperty("beforeLaunch").GetBoolean());
        Assert.Equal(JsonValueKind.Null, report.GetProperty("kpis").ValueKind);
        Assert.Equal(0, report.GetProperty("series").GetArrayLength());
        Assert.Equal(0, report.GetProperty("channels").GetArrayLength());
        Assert.Equal(0, report.GetProperty("categories").GetArrayLength());
        Assert.True(page.GetProperty("period").GetProperty("beforeLaunch").GetBoolean());
        Assert.Equal(0, page.GetProperty("items").GetArrayLength());
        Assert.Equal(0, page.GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("sum").ValueKind);
    }

    [Fact]
    public async Task Costs_follow_the_cost_date_and_match_the_expense_list_gross_total_and_withdrawals_do_not_count()
    {
        await using var test = await LateAsync();
        await CreateExpenseAsync(test, ExpensesTest.Input(costDate: "2026-11-04", dueDate: null, paidOn: "2026-11-05", grossAmount: "300.00", netAmount: null));
        await CreateExpenseAsync(test, ExpensesTest.Input(category: "marketing", costDate: "2026-11-10", dueDate: "2026-12-20", grossAmount: "500.00", netAmount: null));
        await CreateExpenseAsync(test, ExpensesTest.Input(costDate: "2026-11-12", dueDate: null, grossAmount: "-200.00", netAmount: null));
        await CreateExpenseAsync(test, ExpensesTest.Input(costDate: "2026-08-20", dueDate: null, paidOn: "2026-11-01", grossAmount: "999.00", netAmount: null));
        using (var recurring = await test.SendAsync(
            HttpMethod.Post, Recurring.Base, Recurring.Input(startsOn: "2026-11-01"), Guid.NewGuid().ToString()))
        {
            Assert.Equal(HttpStatusCode.Created, recurring.StatusCode);
        }

        using (var withdrawal = await test.SendAsync(
            HttpMethod.Post, "/api/owner-withdrawals", new { person = "Właściciel A", amount = "2000.00", withdrawnOn = "2026-11-15", method = "cash" }, Guid.NewGuid().ToString()))
        {
            Assert.Equal(HttpStatusCode.Created, withdrawal.StatusCode);
        }

        var report = await test.GetJsonAsync($"{Sales}?period=custom&from=2026-08-01&to=2026-11-30");

        var period = report.GetProperty("period");
        Assert.Equal("2026-09-01", period.GetProperty("effectiveFrom").GetString());
        Assert.Equal("month", period.GetProperty("granularity").GetString());
        var kpis = report.GetProperty("kpis");
        Assert.Equal("900.00", kpis.GetProperty("costs").GetProperty("value").GetString());
        Assert.Equal("-900.00", kpis.GetProperty("result").GetProperty("value").GetString());
        var categories = report.GetProperty("categories").EnumerateArray()
            .ToDictionary(c => c.GetProperty("category").GetString()!, c => c.GetProperty("amount").GetString());
        Assert.Equal(["materialPurchase", "cutting", "hdsTransport", "marketing", "otherOperations", "vatTax", "ppeTax"], categories.Keys);
        Assert.Equal("100.00", categories["cutting"]);
        Assert.Equal("800.00", categories["marketing"]);
        Assert.Equal("0.00", categories["vatTax"]);
        Assert.Equal(3, report.GetProperty("series").GetArrayLength()); // wrzesień–listopad

        var list = $"/api/expenses?costDateFrom={period.GetProperty("effectiveFrom").GetString()}&costDateTo={period.GetProperty("to").GetString()}";
        Assert.Equal("900.00", (await test.GetJsonAsync(list)).GetProperty("grossTotal").GetString());
        Assert.Equal("100.00", (await test.GetJsonAsync($"{list}&category=cutting")).GetProperty("grossTotal").GetString());
        Assert.Equal("800.00", (await test.GetJsonAsync($"{list}&category=marketing")).GetProperty("grossTotal").GetString());
    }

    [Fact]
    public async Task More_than_ten_thousand_rows_return_422_on_both_endpoints()
    {
        await using var test = await DefaultAsync();
        await test.ExecuteSqlAsync(
            "INSERT INTO expenses.Expenses (Id, Description, Category, CostDate, GrossAmount, NetAmount, CreatedAtUtc, UpdatedAtUtc) "
            + "SELECT TOP (10001) NEWID(), N'Test', N'Cutting', '2026-11-04', 1.00, NULL, SYSUTCDATETIME(), SYSUTCDATETIME() "
            + "FROM sys.all_objects a CROSS JOIN sys.all_objects b;");

        using (var costs = await test.SendAsync(HttpMethod.Get, Sales))
        {
            await OrderEntryRequests.AssertProblemAsync(costs, HttpStatusCode.UnprocessableEntity, ReportingProblems.ReportRangeTooLargeCode);
        }

        // Zestawienie nie czyta kosztów.
        Assert.Equal(0, (await test.GetJsonAsync(Components)).GetProperty("total").GetInt32());

        await test.ExecuteSqlAsync("DELETE FROM expenses.Expenses;");
        var order = await test.CreateOrderAsync(OrderEntryRequests.Order("1000.00", orderDate: "2026-11-04"));
        await test.ExecuteSqlAsync(
            "INSERT INTO orders.SalesCorrections (Id, OrderId, Amount, CorrectedOn, Reason, CreatedAtUtc, UpdatedAtUtc) "
            + $"SELECT TOP (10000) NEWID(), '{order.Id}', 0.01, '2026-11-04', N'Korekta testowa', SYSUTCDATETIME(), SYSUTCDATETIME() "
            + "FROM sys.all_objects a CROSS JOIN sys.all_objects b;");

        using (var sales = await test.SendAsync(HttpMethod.Get, Sales))
        {
            await OrderEntryRequests.AssertProblemAsync(sales, HttpStatusCode.UnprocessableEntity, ReportingProblems.ReportRangeTooLargeCode);
        }

        using var components = await test.SendAsync(HttpMethod.Get, Components);
        await OrderEntryRequests.AssertProblemAsync(components, HttpStatusCode.UnprocessableEntity, ReportingProblems.ReportRangeTooLargeCode);
        using var again = await test.SendAsync(HttpMethod.Get, Components);
        Assert.Equal(ReportingProblems.ReportRangeTooLargeDetail, (await OrderEntryRequests.Json(again)).GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData(Sales, "period=week", "period")]
    [InlineData(Sales, "period=custom&from=2026-11-02&to=2026-11-01", "from")]
    [InlineData(Sales, "period=custom&from=2026-11-01", "to")]
    [InlineData(Sales, "period=custom&from=2026-11-01&to=2027-11-06", "to")]
    [InlineData(Sales, "period=year&year=2027", "year")]
    [InlineData(Components, "period=custom&from=2026-11-02&to=2026-11-01", "from")]
    [InlineData(Components, "channel=allegro", "channel")]
    [InlineData(Components, "kind=refund", "kind")]
    [InlineData(Components, "direction=up", "direction")]
    [InlineData(Components, "page=0", "page")]
    [InlineData(Components, "pageSize=101", "pageSize")]
    public async Task Invalid_parameters_return_400_with_the_key(string path, string query, string key)
    {
        await using var test = await DefaultAsync();

        using var response = await test.SendAsync(HttpMethod.Get, $"{path}?{query}");

        Assert.Equal([key], await OrderEntryRequests.ErrorKeysAsync(response));
    }

    [Fact]
    public async Task End_date_one_year_from_today_is_accepted()
    {
        await using var test = await DefaultAsync();

        var report = await test.GetJsonAsync($"{Sales}?period=custom&from=2026-11-01&to=2027-11-05");

        Assert.Equal("month", report.GetProperty("period").GetProperty("granularity").GetString());
        Assert.Equal(13, report.GetProperty("series").GetArrayLength());
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        await using var test = await DefaultAsync();
        using var anonymous = AuthClient.Create(test.Factory);

        foreach (var path in new[] { Sales, Components })
        {
            using var response = await anonymous.Http.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Exactly_one_sales_source_and_one_cost_source_are_registered()
    {
        await using var test = await DefaultAsync();
        using var scope = test.Factory.Services.CreateScope();

        Assert.Single(scope.ServiceProvider.GetServices<ISalesComponentSource>());
        Assert.Single(scope.ServiceProvider.GetServices<ICostSource>());
    }

    private static void AssertAmountKpi(JsonElement kpi, string value, string status, string? @base, string? difference, string? percent)
    {
        Assert.Equal(value, kpi.GetProperty("value").GetString());
        var comparison = kpi.GetProperty("comparison");
        Assert.Equal(status, comparison.GetProperty("status").GetString());
        Assert.Equal(@base, comparison.GetProperty("base").GetString());
        Assert.Equal(difference, comparison.GetProperty("difference").GetString());
        Assert.Equal(percent, comparison.GetProperty("percent").GetString());
    }

    private static decimal Amount(JsonElement text) => decimal.Parse(text.GetString()!, CultureInfo.InvariantCulture);

    private static async Task<string?> SalesValueAsync(OrdersTest test, string query) =>
        (await test.GetJsonAsync($"{Sales}?{query}")).GetProperty("kpis").GetProperty("sales").GetProperty("value").GetString();

    private static async Task<string> VersionAsync(OrdersTest test, Guid orderId) =>
        (await test.GetJsonAsync($"/api/orders/{orderId}")).GetProperty("version").GetString()!;

    private static async Task<CreatedEntry> AddRefundAsync(OrdersTest test, Guid orderId, string amount, string kind, string date)
    {
        using var response = await OrderEntryRequests.SendRefundAsync(test, orderId, OrderEntryRequests.Refund(amount, kind, date));
        return await OrderEntryRequests.CreatedAsync(response);
    }

    private static async Task CreateExpenseAsync(OrdersTest test, object body)
    {
        using var response = await test.SendAsync(HttpMethod.Post, "/api/expenses", body, Guid.NewGuid().ToString());
        await OrderEntryRequests.CreatedAsync(response);
    }

    /// <summary>Zamówienie ze sklepu z listopada (wstawione bezpośrednio) z podanym statusem sklepu.</summary>
    private static async Task<Guid> InsertShopOrderAsync(OrdersTest test, string shopStatus, string value, int number)
    {
        var id = Guid.NewGuid();
        await test.ExecuteSqlAsync(
            "INSERT INTO orders.Orders (Id, Number, Source, ExternalId, Channel, OrderDate, Value, FulfillmentStatus, ShopStatus, "
            + "CustomerKind, CustomerName, Suppliers, ZoReferences, HasPhotos, HasReview, CreatedAtUtc, UpdatedAtUtc) "
            + $"VALUES ('{id}', N'ZR-2026-90{number:00}', N'WooCommerce', N'20{number:00}', N'Shop', '2026-11-05', {value}, N'InProgress', N'{shopStatus}', "
            + "N'Person', N'Sklepowy Klient Testowy', N'[]', N'[]', 0, 0, SYSUTCDATETIME(), SYSUTCDATETIME());");
        return id;
    }
}
