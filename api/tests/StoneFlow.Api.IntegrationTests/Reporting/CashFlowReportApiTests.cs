using System.Net;
using System.Text.Json;
using StoneFlow.Api.IntegrationTests.Expenses;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Api.IntegrationTests.Orders;
using StoneFlow.Reporting.Api;

namespace StoneFlow.Api.IntegrationTests.Reporting;

/// <summary>
/// SF-20: raport przepływów pieniężnych na prawdziwej bazie z danymi z Orders i Expenses —
/// kształt odpowiedzi, uzgodnienie z listą „Operacje” (FIN-05), rozdzielenie sprzedaży od pieniędzy
/// (FIN-07), wydatek ujemny i bez daty zapłaty, przełom uruchomienia, bezpiecznik, walidacja i
/// dostęp. Dane są fikcyjne.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class CashFlowReportApiTests(SqlServerFixture sqlServer)
{
    private const string CashFlow = "/api/reports/cash-flow";

    /// <summary>Zegar 10.12.2026 i data uruchomienia 1.09.2026.</summary>
    private Task<OrdersTest> LateAsync() => OrdersTest.CreateAsync(
        sqlServer,
        new DateTimeOffset(2026, 12, 10, 12, 0, 0, TimeSpan.Zero),
        settings: new Dictionary<string, string?> { ["StoneFlow:LaunchDate"] = "2026-09-01" });

    // Domyślnie: zegar 5.11.2026, data uruchomienia 3.11.2026.
    private Task<OrdersTest> DefaultAsync() => OrdersTest.CreateAsync(sqlServer);

    [Fact]
    public async Task Response_has_the_report_shape_without_a_balance()
    {
        // I-CF-1
        await using var test = await DefaultAsync();
        var order = await test.CreateOrderAsync(OrderEntryRequests.Order("1000.00"));
        await OrderEntryRequests.AddPaymentAsync(test, order.Id, "400.00", "2026-11-04");
        await AddWithdrawalAsync(test, "100.00", "2026-11-05");

        var report = await test.GetJsonAsync(CashFlow);

        var period = report.GetProperty("period");
        Assert.Equal("currentMonth", period.GetProperty("kind").GetString());
        Assert.Equal("2026-11-01", period.GetProperty("from").GetString());
        Assert.Equal("2026-11-30", period.GetProperty("to").GetString());
        Assert.Equal("2026-11-03", period.GetProperty("effectiveFrom").GetString());
        Assert.Equal("2026-11-03", period.GetProperty("launchDate").GetString());
        Assert.False(period.GetProperty("beforeLaunch").GetBoolean());
        Assert.True(period.GetProperty("inProgress").GetBoolean());
        Assert.Equal("day", period.GetProperty("granularity").GetString());
        var comparison = report.GetProperty("comparison");
        Assert.Equal("2026-10-01", comparison.GetProperty("from").GetString());
        Assert.Equal("2026-10-31", comparison.GetProperty("to").GetString());

        var kpis = report.GetProperty("kpis");
        Assert.Equal(["inflows", "outflows", "change"], kpis.EnumerateObject().Select(p => p.Name));
        AssertAmountKpi(kpis.GetProperty("inflows"), "400.00", "noBasis", null, null, null);
        AssertAmountKpi(kpis.GetProperty("outflows"), "100.00", "noBasis", null, null, null);
        AssertAmountKpi(kpis.GetProperty("change"), "300.00", "noBasis", null, null, null);

        var series = report.GetProperty("series").EnumerateArray().ToList();
        Assert.Equal(28, series.Count); // 03.11–30.11
        Assert.Equal(["from", "to", "inflows", "outflows", "change"], series[0].EnumerateObject().Select(p => p.Name));
        Assert.Equal("2026-11-04", series[1].GetProperty("from").GetString());
        Assert.Equal("400.00", series[1].GetProperty("inflows").GetString());

        Assert.Equal(
            [("expense", "0.00"), ("withdrawal", "100.00"), ("refund", "0.00")],
            OutflowsByKind(report));
        Assert.Equal(2, report.GetProperty("recentOperations").GetArrayLength());
        Assert.Equal(test.Time.GetUtcNow(), report.GetProperty("calculatedAtUtc").GetDateTimeOffset());

        Assert.DoesNotContain(PropertyNames(report), name =>
            name.Contains("balance", StringComparison.OrdinalIgnoreCase) || name.Contains("saldo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Current_month_and_custom_range_across_launch_agree_with_the_operations_list()
    {
        // I-CF-2 (kryterium 2): zegar 5.11, uruchomienie 3.11.
        await using var test = await DefaultAsync();
        var order = await test.CreateOrderAsync(OrderEntryRequests.Order("1000.00"));
        await OrderEntryRequests.AddPaymentAsync(test, order.Id, "400.00", "2026-11-02"); // sprzed uruchomienia
        await OrderEntryRequests.AddPaymentAsync(test, order.Id, "300.00", "2026-11-03");
        for (var i = 0; i < 9; i++)
        {
            await OrderEntryRequests.AddPaymentAsync(test, order.Id, "10.00", "2026-11-04");
        }

        await AddRefundAsync(test, order.Id, "50.00", "salesReturn", "2026-11-04");
        await AddRefundAsync(test, order.Id, "20.00", "moneyReturn", "2026-11-05");
        await CreateExpenseAsync(test, ExpensesTest.Input(paidOn: "2026-11-04", grossAmount: "-200.00", netAmount: null));
        await CreateExpenseAsync(test, ExpensesTest.Input(paidOn: "2026-11-05", grossAmount: "500.00", netAmount: null));
        await AddWithdrawalAsync(test, "1000.00", "2026-11-05");

        var month = await AssertAgreesWithOperationsAsync(test, string.Empty);
        AssertAmountKpi(month.GetProperty("kpis").GetProperty("inflows"), "390.00", "noBasis", null, null, null);
        Assert.Equal("1370.00", month.GetProperty("kpis").GetProperty("outflows").GetProperty("value").GetString());
        Assert.Equal(10, month.GetProperty("recentOperations").GetArrayLength());

        var custom = await AssertAgreesWithOperationsAsync(test, "?period=custom&from=2026-10-25&to=2026-11-04");
        Assert.Equal("2026-11-03", custom.GetProperty("period").GetProperty("effectiveFrom").GetString());
        Assert.Equal("390.00", custom.GetProperty("kpis").GetProperty("inflows").GetProperty("value").GetString());
        Assert.Equal("-150.00", custom.GetProperty("kpis").GetProperty("outflows").GetProperty("value").GetString());
    }

    [Fact]
    public async Task Year_agrees_with_the_operations_list()
    {
        // I-CF-2: zegar 10.12, uruchomienie 1.09 — rok przecina uruchomienie.
        await using var test = await LateAsync();
        var order = await test.CreateOrderAsync(OrderEntryRequests.Order("5000.00", orderDate: "2026-08-20"));
        await OrderEntryRequests.AddPaymentAsync(test, order.Id, "999.00", "2026-08-31"); // sprzed uruchomienia
        await OrderEntryRequests.AddPaymentAsync(test, order.Id, "100.00", "2026-09-01");
        for (var i = 0; i < 5; i++)
        {
            await OrderEntryRequests.AddPaymentAsync(test, order.Id, "20.00", "2026-10-15");
        }

        await OrderEntryRequests.AddPaymentAsync(test, order.Id, "300.00", "2026-12-10");
        await AddRefundAsync(test, order.Id, "40.00", "salesReturn", "2026-10-16");
        await CreateExpenseAsync(test, ExpensesTest.Input(costDate: "2026-11-01", dueDate: null, paidOn: "2026-11-20", grossAmount: "-200.00", netAmount: null));
        await CreateExpenseAsync(test, ExpensesTest.Input(costDate: "2026-11-01", dueDate: null, paidOn: "2026-11-21", grossAmount: "500.00", netAmount: null));
        await AddWithdrawalAsync(test, "700.00", "2026-12-01");
        await AddWithdrawalAsync(test, "50.00", "2026-12-10");

        var year = await AssertAgreesWithOperationsAsync(test, "?period=year");

        Assert.Equal("2026-09-01", year.GetProperty("period").GetProperty("effectiveFrom").GetString());
        Assert.Equal("month", year.GetProperty("period").GetProperty("granularity").GetString());
        Assert.Equal(4, year.GetProperty("series").GetArrayLength()); // wrzesień–grudzień
        Assert.Equal("500.00", year.GetProperty("kpis").GetProperty("inflows").GetProperty("value").GetString());
        Assert.Equal("1090.00", year.GetProperty("kpis").GetProperty("outflows").GetProperty("value").GetString());
        Assert.Equal(
            [("expense", "300.00"), ("withdrawal", "750.00"), ("refund", "40.00")],
            OutflowsByKind(year));
        Assert.Equal(10, year.GetProperty("recentOperations").GetArrayLength());
    }

    [Fact]
    public async Task Unpaid_order_gives_no_inflows_and_a_later_payment_does_not_create_sales()
    {
        // I-CF-3 (kryterium 1): zegar 10.12, uruchomienie 1.09.
        await using var test = await LateAsync();
        var order = await test.CreateOrderAsync(OrderEntryRequests.Order("1000.00", orderDate: "2026-11-04"));

        Assert.Equal("0.00", await InflowsAsync(test, "?period=previousMonth"));

        await OrderEntryRequests.AddPaymentAsync(test, order.Id, "300.00", "2026-12-05");

        Assert.Equal("300.00", await InflowsAsync(test, string.Empty));
        Assert.Equal("0.00", await InflowsAsync(test, "?period=previousMonth"));
        Assert.Equal("1000.00", await SalesAsync(test, "?period=previousMonth"));
        Assert.Equal("0.00", await SalesAsync(test, string.Empty));
    }

    [Fact]
    public async Task Negative_paid_expense_lowers_its_item_and_the_outflows()
    {
        // I-CF-4 (kryterium 4)
        await using var test = await DefaultAsync();
        await CreateExpenseAsync(test, ExpensesTest.Input(paidOn: "2026-11-04", grossAmount: "-200.00", netAmount: null));
        await CreateExpenseAsync(test, ExpensesTest.Input(paidOn: "2026-11-05", grossAmount: "500.00", netAmount: null));

        var report = await test.GetJsonAsync(CashFlow);

        Assert.Equal("300.00", report.GetProperty("kpis").GetProperty("outflows").GetProperty("value").GetString());
        Assert.Equal("-300.00", report.GetProperty("kpis").GetProperty("change").GetProperty("value").GetString());
        Assert.Equal("300.00", OutflowsByKind(report)[0].Amount);
        var negative = report.GetProperty("recentOperations").EnumerateArray().Single(i => i.GetProperty("date").GetString() == "2026-11-04");
        Assert.Equal("-200.00", negative.GetProperty("amount").GetString());
        Assert.Equal("outflow", negative.GetProperty("direction").GetString());
    }

    [Fact]
    public async Task Expense_counts_only_while_paid_by_the_payment_date()
    {
        // I-CF-5 (kryterium 3)
        await using var test = await DefaultAsync();
        var expense = await CreateExpenseAsync(test, ExpensesTest.Input());

        Assert.Equal("0.00", await OutflowsAsync(test));

        using var paid = await test.SendAsync(HttpMethod.Post, $"/api/expenses/{expense.Id}/mark-paid", new { paidOn = "2026-11-04", version = expense.Version });
        var paidVersion = await OrderEntryRequests.VersionAsync(paid);
        var report = await test.GetJsonAsync(CashFlow);
        Assert.Equal("1234.50", report.GetProperty("kpis").GetProperty("outflows").GetProperty("value").GetString());
        Assert.Equal("1234.50", report.GetProperty("series")[1].GetProperty("outflows").GetString()); // 04.11

        using var unmarked = await test.SendAsync(HttpMethod.Post, $"/api/expenses/{expense.Id}/unmark-paid", new { version = paidVersion });
        await OrderEntryRequests.VersionAsync(unmarked);
        Assert.Equal("0.00", await OutflowsAsync(test));
    }

    [Fact]
    public async Task Sales_correction_is_not_a_cash_operation()
    {
        // I-CF-6
        await using var test = await DefaultAsync();
        var order = await test.CreateOrderAsync(OrderEntryRequests.Order("1000.00", orderDate: "2026-11-04"));
        await OrderEntryRequests.AddCorrectionAsync(test, order.Id, "100.00", "2026-11-04");

        var report = await test.GetJsonAsync(CashFlow);

        AssertKpiValues(report, "0.00", "0.00", "0.00");
        Assert.Empty(report.GetProperty("recentOperations").EnumerateArray());
    }

    [Fact]
    public async Task Operations_count_from_launch_by_their_own_date_and_the_base_is_unavailable()
    {
        // I-CF-7 (kryterium 5): zamówienie sprzed uruchomienia (28.10), uruchomienie 3.11.
        await using var test = await DefaultAsync();
        var order = await test.CreateOrderAsync(OrderEntryRequests.Order("1000.00"));
        await OrderEntryRequests.AddPaymentAsync(test, order.Id, "600.00", "2026-11-03");
        await OrderEntryRequests.AddPaymentAsync(test, order.Id, "400.00", "2026-11-02");
        await AddRefundAsync(test, order.Id, "100.00", "salesReturn", "2026-11-04");
        await AddRefundAsync(test, order.Id, "50.00", "moneyReturn", "2026-11-04");

        var report = await test.GetJsonAsync(CashFlow);

        Assert.Equal("2026-11-03", report.GetProperty("period").GetProperty("effectiveFrom").GetString());
        var comparison = report.GetProperty("comparison");
        Assert.False(comparison.GetProperty("available").GetBoolean());
        Assert.Equal("beforeLaunch", comparison.GetProperty("reason").GetString());
        var kpis = report.GetProperty("kpis");
        AssertAmountKpi(kpis.GetProperty("inflows"), "600.00", "noBasis", null, null, null);
        AssertAmountKpi(kpis.GetProperty("outflows"), "150.00", "noBasis", null, null, null);
        AssertAmountKpi(kpis.GetProperty("change"), "450.00", "noBasis", null, null, null);
        Assert.Equal("150.00", OutflowsByKind(report)[2].Amount);
        Assert.Equal(3, report.GetProperty("recentOperations").GetArrayLength());
    }

    [Fact]
    public async Task Period_entirely_before_launch_has_no_cards_and_empty_lists()
    {
        // I-CF-8
        await using var test = await DefaultAsync();
        var order = await test.CreateOrderAsync(OrderEntryRequests.Order("1000.00"));
        await OrderEntryRequests.AddPaymentAsync(test, order.Id, "100.00", "2026-10-30");

        var report = await test.GetJsonAsync($"{CashFlow}?period=custom&from=2026-10-01&to=2026-10-31");

        Assert.True(report.GetProperty("period").GetProperty("beforeLaunch").GetBoolean());
        Assert.Equal(JsonValueKind.Null, report.GetProperty("kpis").ValueKind);
        Assert.Empty(report.GetProperty("series").EnumerateArray());
        Assert.Empty(report.GetProperty("outflowsByKind").EnumerateArray());
        Assert.Empty(report.GetProperty("recentOperations").EnumerateArray());
    }

    [Fact]
    public async Task More_than_ten_thousand_operations_return_422()
    {
        // I-CF-9
        await using var test = await DefaultAsync();
        await InsertWithdrawalsAsync(test, 10_000);

        Assert.Equal("10000.00", await OutflowsAsync(test));

        await InsertWithdrawalsAsync(test, 1);
        using var tooMany = await test.SendAsync(HttpMethod.Get, CashFlow);
        await OrderEntryRequests.AssertProblemAsync(tooMany, HttpStatusCode.UnprocessableEntity, ReportingProblems.ReportRangeTooLargeCode);
        using var again = await test.SendAsync(HttpMethod.Get, CashFlow);
        Assert.Equal(ReportingProblems.ReportRangeTooLargeDetail, (await OrderEntryRequests.Json(again)).GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("period=week", "period")]
    [InlineData("period=custom&from=2026-11-02&to=2026-11-01", "from")]
    [InlineData("period=custom&from=2026-11-01", "to")]
    [InlineData("period=custom&from=2026-11-01&to=2027-11-06", "to")]
    [InlineData("period=year&year=2027", "year")]
    public async Task Invalid_parameters_return_400_with_the_key(string query, string key)
    {
        // I-CF-9
        await using var test = await DefaultAsync();

        using var response = await test.SendAsync(HttpMethod.Get, $"{CashFlow}?{query}");

        Assert.Equal([key], await OrderEntryRequests.ErrorKeysAsync(response));
    }

    [Fact]
    public async Task Owner_withdrawal_is_only_an_outflow_and_does_not_touch_sales()
    {
        // I-CF-10
        await using var test = await DefaultAsync();
        await AddWithdrawalAsync(test, "2000.00", "2026-11-05");

        var report = await test.GetJsonAsync(CashFlow);
        var sales = (await test.GetJsonAsync("/api/reports/sales")).GetProperty("kpis");

        AssertKpiValues(report, "0.00", "2000.00", "-2000.00");
        Assert.Equal([("expense", "0.00"), ("withdrawal", "2000.00"), ("refund", "0.00")], OutflowsByKind(report));
        Assert.Equal("0.00", sales.GetProperty("sales").GetProperty("value").GetString());
        Assert.Equal("0.00", sales.GetProperty("costs").GetProperty("value").GetString());
        Assert.Equal("0.00", sales.GetProperty("result").GetProperty("value").GetString());
    }

    [Fact]
    public async Task Period_without_operations_has_real_zeros()
    {
        // I-CF-10
        await using var test = await DefaultAsync();

        var report = await test.GetJsonAsync(CashFlow);

        AssertKpiValues(report, "0.00", "0.00", "0.00");
        Assert.Equal([("expense", "0.00"), ("withdrawal", "0.00"), ("refund", "0.00")], OutflowsByKind(report));
        Assert.All(report.GetProperty("series").EnumerateArray(), point => Assert.Equal("0.00", point.GetProperty("change").GetString()));
        Assert.Empty(report.GetProperty("recentOperations").EnumerateArray());
    }

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        // I-CF-11
        await using var test = await DefaultAsync();
        using var anonymous = AuthClient.Create(test.Factory);

        using var response = await anonymous.Http.GetAsync(new Uri(CashFlow, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Karty, podział i ostatnie operacje raportu = sumy i pierwsza strona listy „Operacje” przy
    /// [efektywny początek, to] okresu (FIN-05).
    /// </summary>
    private static async Task<JsonElement> AssertAgreesWithOperationsAsync(OrdersTest test, string query)
    {
        var report = await test.GetJsonAsync($"{CashFlow}{query}");
        var period = report.GetProperty("period");
        var list = $"/api/operations?from={period.GetProperty("effectiveFrom").GetString()}&to={period.GetProperty("to").GetString()}";
        var kpis = report.GetProperty("kpis");

        var all = (await test.GetJsonAsync(list)).GetProperty("totals");
        AssertKpiValues(report, all.GetProperty("inflows").GetString()!, all.GetProperty("outflows").GetString()!, all.GetProperty("change").GetString()!);

        var inflows = (await test.GetJsonAsync($"{list}&kind=payment")).GetProperty("totals");
        Assert.Equal(inflows.GetProperty("inflows").GetString(), kpis.GetProperty("inflows").GetProperty("value").GetString());

        var outflows = (await test.GetJsonAsync($"{list}&kind=refund&kind=expense&kind=withdrawal")).GetProperty("totals");
        Assert.Equal(outflows.GetProperty("outflows").GetString(), kpis.GetProperty("outflows").GetProperty("value").GetString());

        foreach (var (kind, amount) in OutflowsByKind(report))
        {
            var ofKind = (await test.GetJsonAsync($"{list}&kind={kind}")).GetProperty("totals");
            Assert.Equal(ofKind.GetProperty("outflows").GetString(), amount);
        }

        var firstPage = (await test.GetJsonAsync($"{list}&pageSize=10")).GetProperty("items").EnumerateArray().Select(i => i.GetRawText());
        Assert.Equal(firstPage, report.GetProperty("recentOperations").EnumerateArray().Select(i => i.GetRawText()));

        return report;
    }

    private static void AssertKpiValues(JsonElement report, string inflows, string outflows, string change)
    {
        var kpis = report.GetProperty("kpis");
        Assert.Equal(inflows, kpis.GetProperty("inflows").GetProperty("value").GetString());
        Assert.Equal(outflows, kpis.GetProperty("outflows").GetProperty("value").GetString());
        Assert.Equal(change, kpis.GetProperty("change").GetProperty("value").GetString());
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

    private static List<(string? Kind, string? Amount)> OutflowsByKind(JsonElement report) =>
        [.. report.GetProperty("outflowsByKind").EnumerateArray().Select(i => (i.GetProperty("kind").GetString(), i.GetProperty("amount").GetString()))];

    private static IEnumerable<string> PropertyNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => PropertyNames(p.Value).Prepend(p.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
        _ => [],
    };

    private static async Task<string?> InflowsAsync(OrdersTest test, string query) =>
        (await test.GetJsonAsync($"{CashFlow}{query}")).GetProperty("kpis").GetProperty("inflows").GetProperty("value").GetString();

    private static async Task<string?> OutflowsAsync(OrdersTest test) =>
        (await test.GetJsonAsync(CashFlow)).GetProperty("kpis").GetProperty("outflows").GetProperty("value").GetString();

    private static async Task<string?> SalesAsync(OrdersTest test, string query) =>
        (await test.GetJsonAsync($"/api/reports/sales{query}")).GetProperty("kpis").GetProperty("sales").GetProperty("value").GetString();

    private static async Task<CreatedEntry> AddRefundAsync(OrdersTest test, Guid orderId, string amount, string kind, string date)
    {
        using var response = await OrderEntryRequests.SendRefundAsync(test, orderId, OrderEntryRequests.Refund(amount, kind, date));
        return await OrderEntryRequests.CreatedAsync(response);
    }

    private static async Task<CreatedEntry> CreateExpenseAsync(OrdersTest test, object body)
    {
        using var response = await test.SendAsync(HttpMethod.Post, "/api/expenses", body, Guid.NewGuid().ToString());
        return await OrderEntryRequests.CreatedAsync(response);
    }

    private static async Task<Guid> AddWithdrawalAsync(OrdersTest test, string amount, string withdrawnOn)
    {
        using var response = await test.SendAsync(
            HttpMethod.Post, "/api/owner-withdrawals", new { person = "Właściciel A", amount, withdrawnOn, method = "cash" }, Guid.NewGuid().ToString());
        return (await OrderEntryRequests.CreatedAsync(response)).Id;
    }

    private static Task InsertWithdrawalsAsync(OrdersTest test, int count) => test.ExecuteSqlAsync(
        "INSERT INTO expenses.OwnerWithdrawals (Id, Person, Amount, WithdrawnOn, Method, CreatedAtUtc, UpdatedAtUtc) "
        + $"SELECT TOP ({count}) NEWID(), N'Właściciel A', 1.00, '2026-11-05', N'Cash', SYSUTCDATETIME(), SYSUTCDATETIME() "
        + "FROM sys.all_objects a CROSS JOIN sys.all_objects b;");
}
