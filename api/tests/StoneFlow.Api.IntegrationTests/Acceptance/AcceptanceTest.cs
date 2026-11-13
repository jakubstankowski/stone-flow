using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Api.IntegrationTests.Expenses;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Api.IntegrationTests.Orders;
using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Api.IntegrationTests.Acceptance;

/// <summary>Wartości obu widoków dla jednego okresu: karty raportu sprzedaży i raportu przepływów.</summary>
internal sealed record Views(
    decimal Sales,
    decimal Costs,
    decimal Result,
    decimal Inflows,
    decimal Outflows,
    decimal Change,
    IReadOnlyDictionary<string, decimal> OutflowsByKind,
    JsonElement SalesReport,
    JsonElement CashFlowReport);

/// <summary>
/// Pomocnicy scenariuszy odbioru MVP (SF-21, PRD §11): baza z datą uruchomienia 15.10.2026 i zegarem
/// 10.12.2026, dane wyłącznie przez HTTP, odczyt obu widoków. Okresy: wrzesień (przed uruchomieniem),
/// październik (przecina uruchomienie), listopad (pełny, „poprzedni miesiąc”), grudzień (bieżący).
/// Dane są fikcyjne.
/// </summary>
internal static class Acceptance
{
    public const string LaunchDate = "2026-10-15";

    /// <summary>„Teraz” scenariuszy: 10.12.2026 12:00 UTC.</summary>
    public static readonly DateTimeOffset Now = new(2026, 12, 10, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Zegar startowy scenariusza wydatku cyklicznego: 5.11.2026 12:00 UTC.</summary>
    public static readonly DateTimeOffset RecurringStart = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Październik (efektywnie od daty uruchomienia).</summary>
    public const string October = "period=custom&from=2026-10-01&to=2026-10-31";

    /// <summary>Listopad — przy zegarze <see cref="Now"/> „poprzedni miesiąc”.</summary>
    public const string November = "period=previousMonth";

    /// <summary>Grudzień — przy zegarze <see cref="Now"/> „bieżący miesiąc”.</summary>
    public const string December = "period=currentMonth";

    public static Task<OrdersTest> CreateAsync(SqlServerFixture sqlServer, DateTimeOffset? now = null) =>
        OrdersTest.CreateAsync(sqlServer, now ?? Now, settings: new Dictionary<string, string?> { ["StoneFlow:LaunchDate"] = LaunchDate });

    public static decimal Money(string? amount) => decimal.Parse(amount!, NumberStyles.Number, CultureInfo.InvariantCulture);

    public static decimal Money(JsonElement amount) => Money(amount.GetString());

    /// <summary>Przestawia zegar (tylko do przodu) i loguje ponownie, bo sesja wygasa po godzinie bezczynności.</summary>
    public static async Task AdvanceToAsync(this OrdersTest test, DateTimeOffset instant)
    {
        test.Time.SetUtcNow(instant);
        using var login = await test.Client.LoginAsync();
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    public static Task<CreatedOrder> OrderAsync(this OrdersTest test, string value, string orderDate) =>
        test.CreateOrderAsync(OrderEntryRequests.Order(value, orderDate: orderDate));

    public static Task<CreatedEntry> PaymentAsync(this OrdersTest test, Guid orderId, string amount, string date) =>
        OrderEntryRequests.AddPaymentAsync(test, orderId, amount, date);

    public static async Task<CreatedEntry> RefundAsync(this OrdersTest test, Guid orderId, string amount, string kind, string date)
    {
        using var response = await OrderEntryRequests.SendRefundAsync(test, orderId, OrderEntryRequests.Refund(amount, kind, date));
        return await OrderEntryRequests.CreatedAsync(response);
    }

    public static Task<CreatedEntry> CorrectionAsync(this OrdersTest test, Guid orderId, string amount, string date) =>
        OrderEntryRequests.AddCorrectionAsync(test, orderId, amount, date);

    public static async Task CancelAsync(this OrdersTest test, Guid orderId)
    {
        var version = (await test.GetJsonAsync($"/api/orders/{orderId}")).GetProperty("version").GetString()!;
        await test.StatusActionAsync(orderId, "cancel", version);
    }

    /// <summary>Zmiana wartości zamówienia ręcznego (pozostałe pola jak przy utworzeniu).</summary>
    public static async Task EditOrderValueAsync(this OrdersTest test, Guid orderId, string value, string orderDate)
    {
        var version = (await test.GetJsonAsync($"/api/orders/{orderId}")).GetProperty("version").GetString()!;
        await test.EditAsync(orderId, OrderEntryRequests.Order(value, version: version, orderDate: orderDate));
    }

    public static async Task<CreatedEntry> ExpenseAsync(
        this OrdersTest test, string grossAmount, string costDate, string? paidOn = null, string? dueDate = null, string category = "cutting")
    {
        var body = ExpensesTest.Input(
            costDate: costDate, dueDate: dueDate, paidOn: paidOn, grossAmount: grossAmount, netAmount: null, category: category);
        using var response = await test.SendAsync(HttpMethod.Post, "/api/expenses", body, Guid.NewGuid().ToString());
        return await OrderEntryRequests.CreatedAsync(response);
    }

    public static async Task MarkPaidAsync(this OrdersTest test, Guid expenseId, string paidOn)
    {
        var version = (await test.GetJsonAsync($"/api/expenses/{expenseId}")).GetProperty("version").GetString()!;
        using var response = await test.SendAsync(HttpMethod.Post, $"/api/expenses/{expenseId}/mark-paid", new { paidOn, version });
        await OrderEntryRequests.VersionAsync(response);
    }

    public static async Task<Guid> WithdrawalAsync(this OrdersTest test, string amount, string withdrawnOn)
    {
        using var response = await test.SendAsync(
            HttpMethod.Post, "/api/owner-withdrawals", new { person = "Właściciel A", amount, withdrawnOn, method = "cash" }, Guid.NewGuid().ToString());
        return (await OrderEntryRequests.CreatedAsync(response)).Id;
    }

    /// <summary>Definicja wydatku cyklicznego; zwraca id i liczbę wystąpień utworzonych przy zapisie.</summary>
    public static async Task<(Guid Id, int Created)> RecurringAsync(this OrdersTest test, int dayOfMonth, string startsOn, string grossAmount)
    {
        var body = Recurring.Input(dayOfMonth: dayOfMonth, startsOn: startsOn, grossAmount: grossAmount, netAmount: null);
        using var response = await test.SendAsync(HttpMethod.Post, Recurring.Base, body, Guid.NewGuid().ToString());
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var json = await AuthClient.ProblemAsync(response);
        return (json.GetProperty("id").GetGuid(), json.GetProperty("createdOccurrences").GetInt32());
    }

    /// <summary>Przebieg zadania <c>recurring-expenses</c> przez runner harmonogramu; musi zakończyć się sukcesem.</summary>
    public static async Task RunRecurringJobAsync(this OrdersTest test)
    {
        var outcome = await test.Factory.Services.GetRequiredService<IJobRunner>()
            .TryRunNowAsync(Recurring.JobKey, TestContext.Current.CancellationToken);
        Assert.Equal(JobRunStatus.Succeeded, Assert.IsType<JobRunOutcome.Completed>(outcome).Status);
    }

    /// <summary>Wystąpienia definicji z listy wydatków: (id, data kosztu), rosnąco po dacie kosztu.</summary>
    public static async Task<IReadOnlyList<(Guid Id, string CostDate)>> OccurrencesAsync(this OrdersTest test, Guid definitionId)
    {
        var page = await test.GetJsonAsync($"/api/expenses?recurringDefinitionId={definitionId}&pageSize=100");
        Assert.Equal(page.GetProperty("items").GetArrayLength(), page.GetProperty("total").GetInt32());
        return
        [
            .. page.GetProperty("items").EnumerateArray()
                .Select(i => (i.GetProperty("id").GetGuid(), i.GetProperty("costDate").GetString()!))
                .OrderBy(i => i.Item2, StringComparer.Ordinal),
        ];
    }

    /// <summary>Karty obu widoków i podział wypływów dla zapytania okresu (np. <see cref="November"/>).</summary>
    public static async Task<Views> ViewsAsync(this OrdersTest test, string periodQuery)
    {
        var sales = await test.GetJsonAsync($"/api/reports/sales?{periodQuery}");
        var cash = await test.GetJsonAsync($"/api/reports/cash-flow?{periodQuery}");
        var salesKpis = sales.GetProperty("kpis");
        var cashKpis = cash.GetProperty("kpis");
        return new Views(
            Money(salesKpis.GetProperty("sales").GetProperty("value")),
            Money(salesKpis.GetProperty("costs").GetProperty("value")),
            Money(salesKpis.GetProperty("result").GetProperty("value")),
            Money(cashKpis.GetProperty("inflows").GetProperty("value")),
            Money(cashKpis.GetProperty("outflows").GetProperty("value")),
            Money(cashKpis.GetProperty("change").GetProperty("value")),
            cash.GetProperty("outflowsByKind").EnumerateArray()
                .ToDictionary(i => i.GetProperty("kind").GetString()!, i => Money(i.GetProperty("amount"))),
            sales,
            cash);
    }

    /// <summary>Suma całego filtra zestawienia składników sprzedaży (strona 1 o rozmiarze 1).</summary>
    public static async Task<decimal> ComponentsSumAsync(this OrdersTest test, string periodQuery) =>
        Money((await test.GetJsonAsync($"/api/reports/sales/components?{periodQuery}&pageSize=1")).GetProperty("sum"));

    /// <summary><c>grossTotal</c> listy wydatków dla zakresu dat kosztu.</summary>
    public static async Task<decimal> ExpensesGrossTotalAsync(this OrdersTest test, string from, string to) =>
        Money((await test.GetJsonAsync($"/api/expenses?costDateFrom={from}&costDateTo={to}")).GetProperty("grossTotal"));

    /// <summary><c>totals</c> listy „Operacje” dla zakresu dat: (wpływy, wypływy, zmiana).</summary>
    public static async Task<(decimal Inflows, decimal Outflows, decimal Change)> OperationsTotalsAsync(this OrdersTest test, string from, string to)
    {
        var totals = (await test.GetJsonAsync($"/api/operations?from={from}&to={to}")).GetProperty("totals");
        return (Money(totals.GetProperty("inflows")), Money(totals.GetProperty("outflows")), Money(totals.GetProperty("change")));
    }

    /// <summary>Wpisy „Historii zmian” z datą biznesową w zakresie (zmiany wprowadzone od 1.12.2026 do 31.01.2027).</summary>
    public static async Task<IReadOnlyList<JsonElement>> ChangeHistoryAsync(this OrdersTest test, string businessFrom, string businessTo) =>
    [
        .. (await test.GetJsonAsync(
                $"/api/change-history?changedFrom=2026-12-01&changedTo=2027-01-31&businessFrom={businessFrom}&businessTo={businessTo}&pageSize=100"))
            .GetProperty("items").EnumerateArray(),
    ];
}
