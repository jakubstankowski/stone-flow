using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Api.IntegrationTests.Expenses;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Api.IntegrationTests.Orders;
using StoneFlow.Reporting.Api;
using StoneFlow.Shared.Application.Operations;
using StoneFlow.Shared.Kernel.Operations;

namespace StoneFlow.Api.IntegrationTests.Reporting;

/// <summary>
/// SF-17: lista „Operacje” na prawdziwej bazie z danymi z Orders i Expenses — rodzaje, opis i
/// źródło, pominięte zapisy, sumy, data uruchomienia (2026-11-03 w testach), walidacja,
/// bezpiecznik liczby operacji i dostęp. Zegar: 5.11.2026. Dane są fikcyjne.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class OperationsApiTests(SqlServerFixture sqlServer)
{
    private const string November = "/api/operations?from=2026-11-01&to=2026-11-30";

    [Fact]
    public async Task Lists_every_kind_with_direction_amount_method_and_source()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(OrderEntryRequests.Order("1000.00"));
        var payment = await OrderEntryRequests.AddPaymentAsync(test, order.Id, "1000.00", "2026-11-04", "blik");
        var salesReturn = await AddRefundAsync(test, order.Id, "200.00", "salesReturn", "2026-11-04", "transfer");
        var moneyReturn = await AddRefundAsync(test, order.Id, "50.00", "moneyReturn", "2026-11-05", "cash");
        var expense = await AddExpenseAsync(test, ExpensesTest.Input(description: "Cięcie płyt", paidOn: "2026-11-04", grossAmount: "300.00", netAmount: null, paymentMethod: null));
        var withdrawal = await AddWithdrawalAsync(test, "Właściciel A", "2000.00", "2026-11-05", "cash");

        var page = await test.GetJsonAsync($"{November}&direction=asc");
        var items = page.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("id").GetGuid());

        Assert.Equal(5, page.GetProperty("total").GetInt32());
        AssertItem(items[payment.Id], "payment", "2026-11-04", "inflow", "1000.00", "blik", null, order.Number, null, "order", order.Id);
        AssertItem(items[salesReturn.Id], "refund", "2026-11-04", "outflow", "200.00", "transfer", "salesReturn", order.Number, null, "order", order.Id);
        AssertItem(items[moneyReturn.Id], "refund", "2026-11-05", "outflow", "50.00", "cash", "moneyReturn", order.Number, null, "order", order.Id);
        AssertItem(items[expense], "expense", "2026-11-04", "outflow", "300.00", null, null, "Cięcie płyt", "cutting", "expense", expense);
        AssertItem(items[withdrawal], "withdrawal", "2026-11-05", "outflow", "2000.00", "cash", null, "Właściciel A", null, "withdrawal", withdrawal);

        // Rosnąco po dacie; remis: wpłata, zwrot, wydatek, wypłata.
        Assert.Equal(
            ["payment", "refund", "expense", "refund", "withdrawal"],
            page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task Sales_corrections_unpaid_expenses_and_orders_without_payments_are_not_listed()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var withCorrection = await test.CreateOrderAsync(OrderEntryRequests.Order("1000.00"));
        await OrderEntryRequests.AddCorrectionAsync(test, withCorrection.Id, "100.00", "2026-11-04");
        await test.CreateOrderAsync(OrderEntryRequests.Order("500.00"));
        var expense = await CreateExpenseAsync(test, ExpensesTest.Input());

        Assert.Equal(0, (await test.GetJsonAsync(November)).GetProperty("total").GetInt32());

        using var paid = await test.SendAsync(HttpMethod.Post, $"/api/expenses/{expense.Id}/mark-paid", new { paidOn = "2026-11-04", version = expense.Version });
        var paidVersion = await OrderEntryRequests.VersionAsync(paid);
        var afterPaid = await test.GetJsonAsync(November);
        var item = Assert.Single(afterPaid.GetProperty("items").EnumerateArray());
        Assert.Equal(expense.Id, item.GetProperty("id").GetGuid());
        Assert.Equal("2026-11-04", item.GetProperty("date").GetString());

        using var unmarked = await test.SendAsync(HttpMethod.Post, $"/api/expenses/{expense.Id}/unmark-paid", new { version = paidVersion });
        await OrderEntryRequests.VersionAsync(unmarked);
        Assert.Equal(0, (await test.GetJsonAsync(November)).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Payments_to_cancelled_and_pending_shop_orders_are_listed()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var cancelled = await test.CreateOrderAsync(OrderEntryRequests.Order("1000.00"));
        var payment = await OrderEntryRequests.AddPaymentAsync(test, cancelled.Id, "300.00", "2026-11-04");
        var version = (await test.GetJsonAsync($"/api/orders/{cancelled.Id}")).GetProperty("version").GetString()!;
        await test.StatusActionAsync(cancelled.Id, "cancel", version);
        var shopOrder = await test.SeedShopOrderAsync();
        await test.ExecuteSqlAsync($"UPDATE orders.Orders SET ShopStatus = N'pending' WHERE Id = '{shopOrder}';");
        var shopPayment = Guid.NewGuid();
        await test.ExecuteSqlAsync(
            "INSERT INTO orders.Payments (Id, OrderId, Amount, PaidOn, Method, CreatedAtUtc, UpdatedAtUtc) "
            + $"VALUES ('{shopPayment}', '{shopOrder}', 250.00, '2026-11-05', N'Transfer', SYSUTCDATETIME(), SYSUTCDATETIME());");

        var ids = await test.IdsAsync(November);

        Assert.Equal([shopPayment, payment.Id], ids);
    }

    [Fact]
    public async Task Totals_cover_the_whole_filter_and_a_negative_expense_lowers_outflows()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(OrderEntryRequests.Order("1000.00"));
        await OrderEntryRequests.AddPaymentAsync(test, order.Id, "1000.00", "2026-11-04");
        await AddWithdrawalAsync(test, "Właściciel A", "2000.00", "2026-11-05", "transfer");
        await AddExpenseAsync(test, ExpensesTest.Input(paidOn: "2026-11-03", grossAmount: "-50.00", netAmount: null));

        foreach (var query in new[] { November, $"{November}&pageSize=1", $"{November}&pageSize=1&page=3&direction=asc" })
        {
            var page = await test.GetJsonAsync(query);
            AssertTotals(page, "1000.00", "1950.00", "-950.00");
            Assert.Equal(3, page.GetProperty("total").GetInt32());
        }

        var expensesOnly = await test.GetJsonAsync($"{November}&kind=expense");
        AssertTotals(expensesOnly, "0.00", "-50.00", "50.00");
        var item = Assert.Single(expensesOnly.GetProperty("items").EnumerateArray());
        Assert.Equal("outflow", item.GetProperty("direction").GetString());
        Assert.Equal("-50.00", item.GetProperty("amount").GetString());

        var paymentsAndWithdrawals = await test.GetJsonAsync($"{November}&kind=payment&kind=withdrawal");
        AssertTotals(paymentsAndWithdrawals, "1000.00", "2000.00", "-1000.00");
    }

    [Fact]
    public async Task Operations_count_from_launch_by_their_own_date_not_the_order_or_cost_date()
    {
        // Data uruchomienia 2026-11-03; zamówienie i koszt wydatku sprzed uruchomienia (URU-01).
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(OrderEntryRequests.Order("1000.00"));
        var onLaunch = await OrderEntryRequests.AddPaymentAsync(test, order.Id, "600.00", "2026-11-03");
        await OrderEntryRequests.AddPaymentAsync(test, order.Id, "400.00", "2026-11-02");
        var refund = await AddRefundAsync(test, order.Id, "100.00", "moneyReturn", "2026-11-03", "transfer");
        var expense = await AddExpenseAsync(test, ExpensesTest.Input(costDate: "2026-10-20", dueDate: null, paidOn: "2026-11-04"));
        await AddWithdrawalAsync(test, "Właściciel A", "500.00", "2026-11-02", "cash");

        var page = await test.GetJsonAsync(November);

        // Malejąco po dacie; remis w dniu uruchomienia: wpłata przed zwrotem.
        Assert.Equal([expense, onLaunch.Id, refund.Id], page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
        Assert.Equal("2026-11-03", page.GetProperty("effectiveFrom").GetString());
        Assert.Equal("2026-11-03", page.GetProperty("launchDate").GetString());
        AssertTotals(page, "600.00", "1334.50", "-734.50");

        var afterLaunch = await test.GetJsonAsync("/api/operations?from=2026-11-04&to=2026-11-30");
        Assert.Equal("2026-11-04", afterLaunch.GetProperty("effectiveFrom").GetString());
        Assert.Equal([expense], afterLaunch.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task Period_entirely_before_launch_is_empty_with_zero_totals()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(OrderEntryRequests.Order("1000.00"));
        await OrderEntryRequests.AddPaymentAsync(test, order.Id, "100.00", "2026-10-30");

        var page = await test.GetJsonAsync("/api/operations?from=2026-10-01&to=2026-10-31");

        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.Equal(0, page.GetProperty("total").GetInt32());
        AssertTotals(page, "0.00", "0.00", "0.00");
        Assert.Equal("2026-11-03", page.GetProperty("effectiveFrom").GetString());
        Assert.Equal("2026-11-03", page.GetProperty("launchDate").GetString());
    }

    [Theory]
    [InlineData("from=2026-11-02&to=2026-11-01", "from")]
    [InlineData("to=2026-11-30", "from")]
    [InlineData("from=2026-11-01", "to")]
    [InlineData("from=2026-11-01&to=30.11.2026", "to")]
    [InlineData("from=2026-11-01&to=2026-11-30&page=0", "page")]
    [InlineData("from=2026-11-01&to=2026-11-30&pageSize=101", "pageSize")]
    [InlineData("from=2026-11-01&to=2026-11-30&kind=salesCorrection", "kind")]
    [InlineData("from=2026-11-01&to=2026-11-30&direction=up", "direction")]
    public async Task Invalid_parameters_return_400_with_the_key(string query, string key)
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);

        using var response = await test.SendAsync(HttpMethod.Get, $"/api/operations?{query}");

        Assert.Contains(key, await OrderEntryRequests.ErrorKeysAsync(response));
    }

    [Fact]
    public async Task Page_beyond_the_end_is_empty_with_real_total_and_totals()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        await AddWithdrawalAsync(test, "Właściciel A", "2000.00", "2026-11-05", "transfer");

        var page = await test.GetJsonAsync($"{November}&page=5");

        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        Assert.Equal(5, page.GetProperty("page").GetInt32());
        Assert.Equal(25, page.GetProperty("pageSize").GetInt32());
        AssertTotals(page, "0.00", "2000.00", "-2000.00");
    }

    [Fact]
    public async Task More_than_ten_thousand_operations_return_422()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        await InsertWithdrawalsAsync(test, 10_000);

        var full = await test.GetJsonAsync(November);
        Assert.Equal(10_000, full.GetProperty("total").GetInt32());
        AssertTotals(full, "0.00", "10000.00", "-10000.00");

        await InsertWithdrawalsAsync(test, 1);
        using var tooMany = await test.SendAsync(HttpMethod.Get, November);
        await OrderEntryRequests.AssertProblemAsync(tooMany, HttpStatusCode.UnprocessableEntity, ReportingProblems.RangeTooLargeCode);
        using var again = await test.SendAsync(HttpMethod.Get, November);
        Assert.Equal(ReportingProblems.RangeTooLargeDetail, (await OrderEntryRequests.Json(again)).GetProperty("detail").GetString());

        // Inne źródło: 10 001 wpłat przy filtrze bez wypłat.
        await test.ExecuteSqlAsync("DELETE FROM expenses.OwnerWithdrawals;");
        var order = await test.CreateOrderAsync(OrderEntryRequests.Order("1000.00"));
        await test.ExecuteSqlAsync(
            "INSERT INTO orders.Payments (Id, OrderId, Amount, PaidOn, Method, CreatedAtUtc, UpdatedAtUtc) "
            + $"SELECT TOP (10001) NEWID(), '{order.Id}', 1.00, '2026-11-05', N'Transfer', SYSUTCDATETIME(), SYSUTCDATETIME() "
            + "FROM sys.all_objects a CROSS JOIN sys.all_objects b;");
        using var payments = await test.SendAsync(HttpMethod.Get, $"{November}&kind=payment&kind=expense");
        await OrderEntryRequests.AssertProblemAsync(payments, HttpStatusCode.UnprocessableEntity, ReportingProblems.RangeTooLargeCode);
    }

    [Fact]
    public async Task Each_kind_has_exactly_one_registered_source()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        using var scope = test.Factory.Services.CreateScope();

        var sources = scope.ServiceProvider.GetServices<ICashOperationSource>().ToList();

        Assert.Equal(2, sources.Count);
        var kinds = sources.SelectMany(s => s.Kinds).ToList();
        Assert.Equal(kinds.Count, kinds.Distinct().Count());
        Assert.Equal(Enum.GetValues<CashOperationKind>().Order(), kinds.Order());
    }

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        using var anonymous = AuthClient.Create(test.Factory);

        using var response = await anonymous.Http.GetAsync(new Uri(November, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static void AssertItem(
        JsonElement item,
        string kind,
        string date,
        string direction,
        string amount,
        string? method,
        string? refundKind,
        string description,
        string? category,
        string sourceType,
        Guid sourceId)
    {
        Assert.Equal(kind, item.GetProperty("kind").GetString());
        Assert.Equal(date, item.GetProperty("date").GetString());
        Assert.Equal(direction, item.GetProperty("direction").GetString());
        Assert.Equal(amount, item.GetProperty("amount").GetString());
        Assert.Equal(method, item.GetProperty("method").GetString());
        Assert.Equal(refundKind, item.GetProperty("refundKind").GetString());
        Assert.Equal(description, item.GetProperty("description").GetString());
        Assert.Equal(description, item.GetProperty("sourceLabel").GetString());
        Assert.Equal(category, item.GetProperty("category").GetString());
        Assert.Equal(sourceType, item.GetProperty("sourceType").GetString());
        Assert.Equal(sourceId, item.GetProperty("sourceId").GetGuid());
    }

    private static void AssertTotals(JsonElement page, string inflows, string outflows, string change)
    {
        var totals = page.GetProperty("totals");
        Assert.Equal(inflows, totals.GetProperty("inflows").GetString());
        Assert.Equal(outflows, totals.GetProperty("outflows").GetString());
        Assert.Equal(change, totals.GetProperty("change").GetString());
    }

    private static async Task<CreatedEntry> AddRefundAsync(OrdersTest test, Guid orderId, string amount, string kind, string date, string method)
    {
        using var response = await OrderEntryRequests.SendRefundAsync(test, orderId, OrderEntryRequests.Refund(amount, kind, date, method));
        return await OrderEntryRequests.CreatedAsync(response);
    }

    private static async Task<CreatedEntry> CreateExpenseAsync(OrdersTest test, object body)
    {
        using var response = await test.SendAsync(HttpMethod.Post, "/api/expenses", body, Guid.NewGuid().ToString());
        return await OrderEntryRequests.CreatedAsync(response);
    }

    private static async Task<Guid> AddExpenseAsync(OrdersTest test, object body) => (await CreateExpenseAsync(test, body)).Id;

    private static async Task<Guid> AddWithdrawalAsync(OrdersTest test, string person, string amount, string withdrawnOn, string method)
    {
        using var response = await test.SendAsync(
            HttpMethod.Post, "/api/owner-withdrawals", new { person, amount, withdrawnOn, method }, Guid.NewGuid().ToString());
        return (await OrderEntryRequests.CreatedAsync(response)).Id;
    }

    private static Task InsertWithdrawalsAsync(OrdersTest test, int count) => test.ExecuteSqlAsync(
        "INSERT INTO expenses.OwnerWithdrawals (Id, Person, Amount, WithdrawnOn, Method, CreatedAtUtc, UpdatedAtUtc) "
        + $"SELECT TOP ({count}) NEWID(), N'Właściciel A', 1.00, '2026-11-05', N'Cash', SYSUTCDATETIME(), SYSUTCDATETIME() "
        + "FROM sys.all_objects a CROSS JOIN sys.all_objects b;");
}
