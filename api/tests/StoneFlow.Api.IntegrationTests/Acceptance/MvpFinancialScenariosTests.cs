using System.Text.Json;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Api.IntegrationTests.Orders;
using static StoneFlow.Api.IntegrationTests.Acceptance.Acceptance;

namespace StoneFlow.Api.IntegrationTests.Acceptance;

/// <summary>
/// SF-21: scenariusze finansowe PRD §11 przechodzone przez API na prawdziwej bazie. Każdy scenariusz
/// sprawdza oba widoki (raport sprzedaży z kartą „Koszty” i raport przepływów) w dotkniętych
/// okresach; test uzgodnienia buduje wszystkie scenariusze na jednej bazie i porównuje karty z
/// rekordami źródłowymi i listami; ostatni test wyjaśnia zmianę minionego okresu „Historią zmian”.
/// Data uruchomienia 15.10.2026, zegar 10.12.2026 (<see cref="Acceptance"/>). Kwoty są fikcyjne i
/// różne w każdym scenariuszu, żeby każdy składnik był rozpoznawalny w sumach.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class MvpFinancialScenariosTests(SqlServerFixture sqlServer)
{
    [Fact]
    public async Task Unpaid_order_raises_sales_but_not_inflows()
    {
        // 36.1
        await using var test = await CreateAsync(sqlServer);
        await SeedUnpaidOrderAsync(test);

        var november = await test.ViewsAsync(November);

        Assert.Equal(1000.00m, november.Sales);
        Assert.Equal(0m, november.Inflows);
        Assert.Equal(0m, (await test.ViewsAsync(December)).Sales);
    }

    [Fact]
    public async Task Payment_in_another_month_is_sales_in_the_order_month_and_inflow_in_the_payment_month()
    {
        // 36.2
        await using var test = await CreateAsync(sqlServer);
        await SeedPaymentInAnotherMonthAsync(test);

        var november = await test.ViewsAsync(November);
        var december = await test.ViewsAsync(December);

        Assert.Equal((2000.00m, 0m), (november.Sales, november.Inflows));
        Assert.Equal((0m, 2000.00m), (december.Sales, december.Inflows));
    }

    [Fact]
    public async Task Sales_correction_lowers_sales_in_its_own_period_and_does_not_touch_cash()
    {
        // 36.3
        await using var test = await CreateAsync(sqlServer);
        await SeedSalesCorrectionAsync(test);

        var november = await test.ViewsAsync(November);
        var december = await test.ViewsAsync(December);

        Assert.Equal(3000.00m, november.Sales);
        Assert.Equal(-200.00m, december.Sales);
        Assert.All(new[] { november, december }, views => Assert.Equal((0m, 0m), (views.Inflows, views.Outflows)));
    }

    [Fact]
    public async Task Sales_return_lowers_sales_and_is_an_outflow()
    {
        // 36.4
        await using var test = await CreateAsync(sqlServer);
        await SeedSalesReturnAsync(test);

        var november = await test.ViewsAsync(November);
        var december = await test.ViewsAsync(December);

        Assert.Equal((400.00m, 400.00m), (november.Sales, november.Inflows));
        Assert.Equal((-30.00m, 30.00m), (december.Sales, december.Outflows));
        Assert.Equal(30.00m, december.OutflowsByKind["refund"]);
    }

    [Fact]
    public async Task Overpayment_return_is_an_outflow_without_changing_sales()
    {
        // 36.5
        await using var test = await CreateAsync(sqlServer);
        await SeedOverpaymentReturnAsync(test);

        var november = await test.ViewsAsync(November);
        var december = await test.ViewsAsync(December);

        Assert.Equal((500.00m, 550.00m), (november.Sales, november.Inflows));
        Assert.Equal((0m, 50.00m), (december.Sales, december.Outflows));
        Assert.Equal(50.00m, december.OutflowsByKind["refund"]);
    }

    [Fact]
    public async Task Goods_returned_without_a_refund_are_a_sales_correction_without_an_outflow()
    {
        // 36.6
        await using var test = await CreateAsync(sqlServer);
        await SeedGoodsReturnedWithoutRefundAsync(test);

        var november = await test.ViewsAsync(November);

        Assert.Equal(540.00m, november.Sales);
        Assert.Equal((0m, 0m), (november.Inflows, november.Outflows));
    }

    [Fact]
    public async Task Supplier_correction_lowers_costs_on_its_cost_date_and_outflows_once_paid()
    {
        // 36.7
        await using var test = await CreateAsync(sqlServer);
        var correction = await SeedSupplierCorrectionAsync(test);

        var unpaid = await test.ViewsAsync(November);
        Assert.Equal((-250.00m, 0m), (unpaid.Costs, unpaid.Outflows));
        Assert.Equal(0m, (await test.ViewsAsync(December)).Outflows);

        await PaySupplierCorrectionAsync(test, correction);

        var november = await test.ViewsAsync(November);
        var december = await test.ViewsAsync(December);
        Assert.Equal((-250.00m, 0m), (november.Costs, november.Outflows));
        Assert.Equal((0m, -250.00m), (december.Costs, december.Outflows));
        Assert.Equal(-250.00m, december.OutflowsByKind["expense"]);
    }

    [Fact]
    public async Task Cancelled_paid_order_leaves_sales_while_its_payment_and_refund_stay_in_cash()
    {
        // 36.8
        await using var test = await CreateAsync(sqlServer);
        var order = await test.OrderAsync("800.00", "2026-11-21");
        await test.PaymentAsync(order.Id, "800.00", "2026-11-22");
        await test.RefundAsync(order.Id, "800.00", "moneyReturn", "2026-11-23");
        Assert.Equal(800.00m, (await test.ViewsAsync(November)).Sales);

        await test.CancelAsync(order.Id);

        var november = await test.ViewsAsync(November);
        Assert.Equal(0m, november.Sales);
        Assert.Equal(0m, await test.ComponentsSumAsync(November));
        Assert.Equal((800.00m, 800.00m), (november.Inflows, november.Outflows));
        Assert.Equal(800.00m, november.OutflowsByKind["refund"]);
    }

    [Fact]
    public async Task Order_open_on_launch_day_counts_only_money_from_the_launch_date()
    {
        // 36.9
        await using var test = await CreateAsync(sqlServer);
        await SeedOrderOpenOnLaunchAsync(test);

        var october = await test.ViewsAsync(October);
        var november = await test.ViewsAsync(November);

        Assert.Equal(LaunchDate, october.SalesReport.GetProperty("period").GetProperty("effectiveFrom").GetString());
        Assert.Equal((0m, 200.00m), (october.Sales, october.Inflows));
        Assert.Equal((0m, 0m, 45.00m), (november.Sales, november.Inflows, november.Outflows));
        Assert.Equal(45.00m, november.OutflowsByKind["refund"]);
    }

    [Fact]
    public async Task Expense_open_on_launch_day_is_not_a_cost_but_its_payment_is_an_outflow()
    {
        // 36.10
        await using var test = await CreateAsync(sqlServer);
        await SeedExpenseOpenOnLaunchAsync(test);

        var october = await test.ViewsAsync(October);

        Assert.Equal((0m, 700.00m), (october.Costs, october.Outflows));
        Assert.Equal(700.00m, october.OutflowsByKind["expense"]);
        Assert.Equal(0m, await test.ExpensesGrossTotalAsync(LaunchDate, "2026-10-31"));
    }

    [Fact]
    public async Task Planned_expense_is_a_cost_but_not_an_outflow()
    {
        // 36.11
        await using var test = await CreateAsync(sqlServer);
        await SeedPlannedExpenseAsync(test);

        var november = await test.ViewsAsync(November);

        Assert.Equal((110.00m, 0m), (november.Costs, november.Outflows));
        Assert.Equal(0m, (await test.ViewsAsync(December)).Outflows);
    }

    [Fact]
    public async Task Recurring_occurrence_is_a_cost_on_its_date_an_outflow_once_paid_and_a_rerun_creates_no_duplicate()
    {
        // 36.12
        await using var test = await CreateAsync(sqlServer, RecurringStart);
        var definition = await SeedRecurringDefinitionAsync(test);
        await test.AdvanceToAsync(Now);
        await test.RunRecurringJobAsync();

        var occurrences = await test.OccurrencesAsync(definition);
        Assert.Equal(["2026-11-01", "2026-12-01"], occurrences.Select(o => o.CostDate));
        var unpaid = await test.ViewsAsync(December);
        Assert.Equal((300.00m, 0m), (unpaid.Costs, unpaid.Outflows));

        await test.RunRecurringJobAsync();
        Assert.Equal(2, (await test.OccurrencesAsync(definition)).Count);

        await PayDecemberOccurrenceAsync(test, definition);

        var december = await test.ViewsAsync(December);
        Assert.Equal((300.00m, 300.00m), (december.Costs, december.Outflows));
        Assert.Equal(300.00m, (await test.ViewsAsync(November)).Costs);
    }

    [Fact]
    public async Task Owner_withdrawal_is_an_outflow_and_not_a_cost()
    {
        // 36.13
        await using var test = await CreateAsync(sqlServer);
        await SeedWithdrawalAsync(test);

        var december = await test.ViewsAsync(December);

        Assert.Equal((0m, 0m, 0m), (december.Sales, december.Costs, december.Result));
        Assert.Equal((1500.00m, -1500.00m), (december.Outflows, december.Change));
        Assert.Equal(1500.00m, december.OutflowsByKind["withdrawal"]);
    }

    [Fact]
    public async Task Both_views_reconcile_with_source_records_for_month_year_and_range_across_launch()
    {
        // 37: wszystkie scenariusze 36.1–36.13 na jednej bazie; oczekiwane sumy policzone z danych
        // wejściowych (komentarz przy SeedAllScenariosAsync).
        await using var test = await CreateAsync(sqlServer, RecurringStart);
        await SeedAllScenariosAsync(test);

        await AssertReconcilesAsync(test, new(November, "2026-11-01", "2026-11-30", 7440.00m, 160.00m, 1750.00m, 0m, 0m, 845.00m));
        await AssertReconcilesAsync(test, new("period=year&year=2026", LaunchDate, "2026-12-10", 7210.00m, 460.00m, 3950.00m, 750.00m, 1500.00m, 925.00m));
        await AssertReconcilesAsync(test, new("period=custom&from=2026-10-01&to=2026-11-30", LaunchDate, "2026-11-30", 7440.00m, 160.00m, 1950.00m, 700.00m, 0m, 845.00m));
    }

    [Fact]
    public async Task Change_of_a_past_period_is_explained_by_change_history()
    {
        // 38
        await using var test = await CreateAsync(sqlServer);
        var order = await test.OrderAsync("1000.00", "2026-11-14");
        var before = await test.ViewsAsync(November);
        Assert.Equal((1000.00m, 0m), (before.Sales, before.Costs));

        await test.AdvanceToAsync(new DateTimeOffset(2027, 1, 5, 12, 0, 0, TimeSpan.Zero));
        await test.EditOrderValueAsync(order.Id, "1250.00", "2026-11-14");
        var expense = await test.ExpenseAsync("40.00", "2026-11-20");

        var after = await test.ViewsAsync("period=custom&from=2026-11-01&to=2026-11-30");
        Assert.Equal(before.Sales + 250.00m, after.Sales);
        Assert.Equal(before.Costs + 40.00m, after.Costs);

        var history = await test.ChangeHistoryAsync("2026-11-01", "2026-11-30");
        var orderChange = Assert.Single(history, e => Is(e, "updated", "order", order.Id));
        var value = Assert.Single(Changes(orderChange), c => c.Field == "value");
        Assert.Equal(("1000.00", "1250.00"), (value.Old, value.New));
        Assert.Equal(after.Sales - before.Sales, Money(value.New) - Money(value.Old));

        var expenseCreation = Assert.Single(history, e => Is(e, "created", "expense", expense.Id));
        var amount = Assert.Single(Changes(expenseCreation), c => c.Field == "grossAmount");
        Assert.Null(amount.Old);
        Assert.Equal(after.Costs - before.Costs, Money(amount.New));
    }

    private sealed record Expected(
        string Period, string EffectiveFrom, string To, decimal Sales, decimal Costs, decimal Inflows, decimal ExpenseOutflows, decimal Withdrawals, decimal Refunds)
    {
        public decimal Outflows => ExpenseOutflows + Withdrawals + Refunds;
    }

    /// <summary>Karty obu widoków = oczekiwane sumy = rekordy źródłowe, listy i wykresy (spec pkt 37).</summary>
    private static async Task AssertReconcilesAsync(OrdersTest test, Expected expected)
    {
        var views = await test.ViewsAsync(expected.Period);

        Assert.Equal(expected.Sales, views.Sales);
        Assert.Equal(expected.Costs, views.Costs);
        Assert.Equal(expected.Sales - expected.Costs, views.Result);
        Assert.Equal(expected.Inflows, views.Inflows);
        Assert.Equal(expected.Outflows, views.Outflows);
        Assert.Equal(expected.Inflows - expected.Outflows, views.Change);
        Assert.Equal(
            new Dictionary<string, decimal> { ["expense"] = expected.ExpenseOutflows, ["withdrawal"] = expected.Withdrawals, ["refund"] = expected.Refunds },
            views.OutflowsByKind);

        // Oba raporty liczą od tego samego efektywnego początku.
        foreach (var report in new[] { views.SalesReport, views.CashFlowReport })
        {
            Assert.Equal(expected.EffectiveFrom, report.GetProperty("period").GetProperty("effectiveFrom").GetString());
            Assert.Equal(expected.To, report.GetProperty("period").GetProperty("to").GetString());
        }

        // Sprzedaż = zestawienie składników; Koszty = lista wydatków i suma kategorii.
        Assert.Equal(views.Sales, await test.ComponentsSumAsync(expected.Period));
        Assert.Equal(views.Costs, await test.ExpensesGrossTotalAsync(expected.EffectiveFrom, expected.To));
        Assert.Equal(views.Costs, views.SalesReport.GetProperty("categories").EnumerateArray().Sum(c => Money(c.GetProperty("amount"))));

        // Przepływy = lista „Operacje”; podział wypływów = wypływy.
        Assert.Equal((views.Inflows, views.Outflows, views.Change), await test.OperationsTotalsAsync(expected.EffectiveFrom, expected.To));
        Assert.Equal(views.Outflows, views.OutflowsByKind.Values.Sum());

        // Wykresy = karty.
        var salesSeries = views.SalesReport.GetProperty("series").EnumerateArray().ToList();
        Assert.Equal(views.Sales, salesSeries.Sum(p => Money(p.GetProperty("sales"))));
        Assert.Equal(views.Costs, salesSeries.Sum(p => Money(p.GetProperty("costs"))));
        Assert.Equal(views.Result, salesSeries.Sum(p => Money(p.GetProperty("result"))));
        var cashSeries = views.CashFlowReport.GetProperty("series").EnumerateArray().ToList();
        Assert.Equal(views.Inflows, cashSeries.Sum(p => Money(p.GetProperty("inflows"))));
        Assert.Equal(views.Outflows, cashSeries.Sum(p => Money(p.GetProperty("outflows"))));
        Assert.Equal(views.Change, cashSeries.Sum(p => Money(p.GetProperty("change"))));
    }

    /// <summary>
    /// Wszystkie scenariusze na jednej bazie (zegar startowy 5.11.2026, potem 10.12.2026). Sumy:
    /// listopad — sprzedaż 1000 + 2000 + 3000 + 400 + 500 + 600 − 60 = 7440, koszty −250 + 110 + 300 = 160,
    /// wpływy 400 + 550 + 800 = 1750, wypływy: zwroty 800 + 45 = 845;
    /// rok (od 15.10) — sprzedaż 7440 − 200 − 30 = 7210, koszty 160 + 300 = 460, wpływy 200 + 1750 + 2000 = 3950,
    /// wypływy: wydatki 700 − 250 + 300 = 750, wypłaty 1500, zwroty 845 + 30 + 50 = 925;
    /// 1.10–30.11 (od 15.10) — sprzedaż 7440, koszty 160, wpływy 200 + 1750 = 1950, wypływy: wydatki 700, zwroty 845.
    /// </summary>
    private static async Task SeedAllScenariosAsync(OrdersTest test)
    {
        var definition = await SeedRecurringDefinitionAsync(test);
        await test.AdvanceToAsync(Now);
        await test.RunRecurringJobAsync();
        await PayDecemberOccurrenceAsync(test, definition);

        await SeedUnpaidOrderAsync(test);
        await SeedPaymentInAnotherMonthAsync(test);
        await SeedSalesCorrectionAsync(test);
        await SeedSalesReturnAsync(test);
        await SeedOverpaymentReturnAsync(test);
        await SeedGoodsReturnedWithoutRefundAsync(test);
        await PaySupplierCorrectionAsync(test, await SeedSupplierCorrectionAsync(test));
        var cancelled = await test.OrderAsync("800.00", "2026-11-21");
        await test.PaymentAsync(cancelled.Id, "800.00", "2026-11-22");
        await test.RefundAsync(cancelled.Id, "800.00", "moneyReturn", "2026-11-23");
        await test.CancelAsync(cancelled.Id);
        await SeedOrderOpenOnLaunchAsync(test);
        await SeedExpenseOpenOnLaunchAsync(test);
        await SeedPlannedExpenseAsync(test);
        await SeedWithdrawalAsync(test);
    }

    // 36.1: sprzedaż XI +1000.
    private static Task SeedUnpaidOrderAsync(OrdersTest test) => test.OrderAsync("1000.00", "2026-11-10");

    // 36.2: sprzedaż XI +2000, wpływ XII +2000.
    private static async Task SeedPaymentInAnotherMonthAsync(OrdersTest test)
    {
        var order = await test.OrderAsync("2000.00", "2026-11-11");
        await test.PaymentAsync(order.Id, "2000.00", "2026-12-02");
    }

    // 36.3: sprzedaż XI +3000, XII −200.
    private static async Task SeedSalesCorrectionAsync(OrdersTest test)
    {
        var order = await test.OrderAsync("3000.00", "2026-11-12");
        await test.CorrectionAsync(order.Id, "200.00", "2026-12-03");
    }

    // 36.4: sprzedaż XI +400, wpływ XI +400; sprzedaż XII −30, wypływ XII (zwrot) +30.
    private static async Task SeedSalesReturnAsync(OrdersTest test)
    {
        var order = await test.OrderAsync("400.00", "2026-11-13");
        await test.PaymentAsync(order.Id, "400.00", "2026-11-14");
        await test.RefundAsync(order.Id, "30.00", "salesReturn", "2026-12-04");
    }

    // 36.5: sprzedaż XI +500, wpływ XI +550 (nadpłata 50); wypływ XII (zwrot) +50.
    private static async Task SeedOverpaymentReturnAsync(OrdersTest test)
    {
        var order = await test.OrderAsync("500.00", "2026-11-16");
        await test.PaymentAsync(order.Id, "550.00", "2026-11-17");
        await test.RefundAsync(order.Id, "50.00", "moneyReturn", "2026-12-05");
    }

    // 36.6: sprzedaż XI +600 − 60.
    private static async Task SeedGoodsReturnedWithoutRefundAsync(OrdersTest test)
    {
        var order = await test.OrderAsync("600.00", "2026-11-18");
        await test.CorrectionAsync(order.Id, "60.00", "2026-11-19");
    }

    // 36.7: koszty XI −250; po opłaceniu wypływ XII (wydatek) −250.
    private static async Task<Guid> SeedSupplierCorrectionAsync(OrdersTest test) =>
        (await test.ExpenseAsync("-250.00", "2026-11-20")).Id;

    private static Task PaySupplierCorrectionAsync(OrdersTest test, Guid expenseId) => test.MarkPaidAsync(expenseId, "2026-12-06");

    // 36.9: zamówienie sprzed uruchomienia (10.10) — bez sprzedaży; wpłata 12.10 poza raportem,
    // wpływ X +200 (20.10); zwrot sprzedaży 5.11 — tylko wypływ XI +45.
    private static async Task SeedOrderOpenOnLaunchAsync(OrdersTest test)
    {
        var order = await test.OrderAsync("9000.00", "2026-10-10");
        await test.PaymentAsync(order.Id, "100.00", "2026-10-12");
        await test.PaymentAsync(order.Id, "200.00", "2026-10-20");
        await test.RefundAsync(order.Id, "45.00", "salesReturn", "2026-11-05");
    }

    // 36.10: koszt 1.10 (sprzed uruchomienia) poza kosztami; wypływ X (wydatek) +700 (20.10).
    private static Task SeedExpenseOpenOnLaunchAsync(OrdersTest test) => test.ExpenseAsync("700.00", "2026-10-01", paidOn: "2026-10-20");

    // 36.11: koszty XI +110, bez wypływu.
    private static Task SeedPlannedExpenseAsync(OrdersTest test) => test.ExpenseAsync("110.00", "2026-11-24", dueDate: "2026-12-20");

    // 36.12 (zegar 5.11): definicja z dniem 1 od 1.11 — wystąpienie XI (koszty XI +300) powstaje przy zapisie.
    private static async Task<Guid> SeedRecurringDefinitionAsync(OrdersTest test)
    {
        var (id, created) = await test.RecurringAsync(dayOfMonth: 1, startsOn: "2026-11-01", grossAmount: "300.00");
        Assert.Equal(1, created);
        return id;
    }

    // 36.12 (zegar 10.12, po zadaniu): wystąpienie XII (koszty XII +300) opłacone 8.12 — wypływ XII (wydatek) +300.
    private static async Task PayDecemberOccurrenceAsync(OrdersTest test, Guid definitionId)
    {
        var december = Assert.Single(await test.OccurrencesAsync(definitionId), o => o.CostDate == "2026-12-01");
        await test.MarkPaidAsync(december.Id, "2026-12-08");
    }

    // 36.13: wypływ XII (wypłata) +1500.
    private static Task SeedWithdrawalAsync(OrdersTest test) => test.WithdrawalAsync("1500.00", "2026-12-07");

    private static bool Is(JsonElement entry, string operation, string type, Guid id) =>
        entry.GetProperty("operation").GetString() == operation
        && entry.GetProperty("object").GetProperty("type").GetString() == type
        && entry.GetProperty("object").GetProperty("id").GetGuid() == id;

    private static List<(string? Field, string? Old, string? New)> Changes(JsonElement entry) =>
    [
        .. entry.GetProperty("changes").EnumerateArray().Select(c => (
            c.GetProperty("field").GetString(),
            c.GetProperty("oldValue").GetString(),
            c.GetProperty("newValue").GetString())),
    ];
}
