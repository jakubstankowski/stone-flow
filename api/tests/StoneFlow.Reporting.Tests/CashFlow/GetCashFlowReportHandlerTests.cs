using Microsoft.Extensions.Time.Testing;
using StoneFlow.Reporting.Application.CashFlow;
using StoneFlow.Reporting.Application.Operations;
using StoneFlow.Reporting.Domain.CashFlow;
using StoneFlow.Reporting.Domain.Periods;
using StoneFlow.Reporting.Tests.Sales;
using StoneFlow.Shared.Application.Operations;
using StoneFlow.Shared.Kernel.Operations;
using static StoneFlow.Reporting.Tests.CashFlow.CashFacts;

namespace StoneFlow.Reporting.Tests.CashFlow;

/// <summary>H-CF-1 … H-CF-9: walidacja, odczyty, przełom uruchomienia, porównanie, bezpiecznik i spójność z listą.</summary>
public sealed class GetCashFlowReportHandlerTests
{
    private const string Today = "2026-11-09";

    private static (GetCashFlowReportHandler Handler, CashSourceLog Log, FakeCashSource Orders, FakeCashSource Expenses) Handler(
        IReadOnlyList<CashOperation>? operations = null,
        string launch = "2026-01-01",
        TimeProvider? clock = null)
    {
        var (orders, expenses, log) = Sources(operations);
        var handler = new GetCashFlowReportHandler(
            [orders, expenses],
            new FixedLaunchDate(DateOnly.Parse(launch)),
            clock ?? ReportFacts.Clock(Today));
        return (handler, log, orders, expenses);
    }

    private static Task<GetCashFlowReportResult> Run(GetCashFlowReportHandler handler, GetCashFlowReportQuery? query = null) =>
        handler.HandleAsync(query ?? new GetCashFlowReportQuery(), TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("week", null, null, null, "period")]
    [InlineData("year", "202", null, null, "year")]
    [InlineData("year", "2027", null, null, "year")]
    [InlineData("custom", null, "2026-11-01", null, "to")]
    [InlineData("custom", null, "2026-11-02", "2026-11-01", "from")]
    [InlineData("custom", null, "2026-11-01", "2027-11-10", "to")]
    public async Task Invalid_period_is_rejected_without_reading_sources(string period, string? year, string? from, string? to, string key)
    {
        // H-CF-1
        var (handler, log, _, _) = Handler();

        var result = await Run(handler, new GetCashFlowReportQuery(period, year, from, to));

        Assert.Null(result.Report);
        Assert.True(result.Errors!.Contains(key));
        Assert.Empty(log.Calls);
    }

    [Fact]
    public async Task Parameters_not_needed_by_the_period_are_ignored()
    {
        // H-CF-1
        var (handler, _, _, _) = Handler();

        var month = await Run(handler, new GetCashFlowReportQuery(null, "abc", "x", "y"));

        Assert.Equal(PeriodKind.CurrentMonth, month.Report!.Period.Kind);
    }

    [Fact]
    public async Task Available_base_is_read_after_the_period_with_all_kinds()
    {
        // H-CF-2
        var (handler, log, _, _) = Handler();

        var report = (await Run(handler)).Report!;

        Assert.True(report.ComparisonAvailable);
        Assert.Equal(["orders", "expenses", "orders", "expenses"], log.Calls.Select(c => c.Source));
        Assert.All(log.Calls.Take(2), call => Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)), (call.From, call.To)));
        Assert.All(log.Calls.Skip(2), call => Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), (call.From, call.To)));
        Assert.Equal(
            Enum.GetValues<CashOperationKind>(),
            log.Calls.Take(2).SelectMany(call => call.Kinds).Order());
    }

    [Fact]
    public async Task Period_before_launch_reads_no_source_and_has_no_cards()
    {
        // H-CF-3
        var (handler, log, _, _) = Handler(launch: "2026-11-03");

        var report = (await Run(handler, new GetCashFlowReportQuery("custom", null, "2026-10-01", "2026-10-31"))).Report!;

        Assert.Empty(log.Calls);
        Assert.True(report.Period.BeforeLaunch);
        Assert.Null(report.Kpis);
        Assert.Empty(report.Series);
        Assert.Empty(report.OutflowsByKind);
        Assert.Empty(report.RecentOperations);
        Assert.False(report.ComparisonAvailable);
    }

    [Fact]
    public async Task Base_before_launch_is_not_read_and_every_card_has_no_basis()
    {
        // H-CF-4
        var (handler, log, _, _) = Handler(
            [CashOp(CashOperationKind.Payment, "2026-11-02", 999m), CashOp(CashOperationKind.Payment, "2026-11-03", 100m)],
            launch: "2026-11-03");

        var report = (await Run(handler)).Report!;

        Assert.Equal(2, log.Calls.Count);
        Assert.All(log.Calls, call => Assert.Equal(new DateOnly(2026, 11, 3), call.From));
        Assert.False(report.ComparisonAvailable);
        Assert.Equal(100m, report.Kpis!.Inflows.Value);
        Assert.All(
            [report.Kpis.Inflows, report.Kpis.Outflows, report.Kpis.Change],
            kpi => Assert.Equal(Comparison.NoBasis, kpi.Comparison));
    }

    [Fact]
    public async Task Each_card_compares_with_its_own_base()
    {
        // H-CF-5: baza wpływów 0 → brak podstawy tylko dla „Wpływów”; baza zmiany −500, bieżąca −250 → +50,0%.
        var (handler, _, _, _) = Handler(
        [
            CashOp(CashOperationKind.PaidExpense, "2026-10-10", 500m),
            CashOp(CashOperationKind.Payment, "2026-11-04", 50m),
            CashOp(CashOperationKind.PaidExpense, "2026-11-05", 300m),
        ]);

        var kpis = (await Run(handler)).Report!.Kpis!;

        Assert.Equal(Comparison.NoBasis, kpis.Inflows.Comparison);
        Assert.Equal(new Comparison(ComparisonStatus.Available, 500m, -200m, -40.0m), kpis.Outflows.Comparison);
        Assert.Equal(-250m, kpis.Change.Value);
        Assert.Equal(new Comparison(ComparisonStatus.Available, -500m, 250m, 50.0m), kpis.Change.Comparison);
    }

    [Fact]
    public async Task More_than_the_maximum_in_the_period_is_too_many_rows()
    {
        // H-CF-6
        var (handler, _, _, _) = Handler([.. Many(CashOperationKind.Payment, 10_001)]);

        var result = await Run(handler);

        Assert.True(result.TooManyRows);
        Assert.Null(result.Report);
    }

    [Fact]
    public async Task More_than_the_maximum_in_the_base_is_too_many_rows()
    {
        // H-CF-6
        var (handler, _, _, _) = Handler([.. Many(CashOperationKind.OwnerWithdrawal, 10_001, "2026-10-05")]);

        Assert.True((await Run(handler)).TooManyRows);
    }

    [Fact]
    public async Task Exactly_the_maximum_gives_a_report()
    {
        // H-CF-6
        var (handler, _, _, _) = Handler([.. Many(CashOperationKind.Payment, 6_000), .. Many(CashOperationKind.PaidExpense, 4_000)]);

        var result = await Run(handler);

        Assert.False(result.TooManyRows);
        Assert.Equal(new CashFlowTotals(6_000m, 4_000m, 2_000m), Totals(result.Report!));
    }

    [Fact]
    public async Task Recent_operations_are_the_first_page_of_the_list()
    {
        // H-CF-7: 12 operacji z remisami dat → 10 w kolejności listy „Operacje”.
        var shared = Guid.Parse("00000000-0000-0000-0000-000000000001");
        List<CashOperation> operations =
        [
            CashOp(CashOperationKind.OwnerWithdrawal, "2026-11-08", 100m, shared),
            CashOp(CashOperationKind.PaidExpense, "2026-11-08", -50m, shared),
            CashOp(CashOperationKind.Refund, "2026-11-08", 10m),
            CashOp(CashOperationKind.Payment, "2026-11-08", 300m),
            .. Enumerable.Range(1, 8).Select(day => CashOp(CashOperationKind.Payment, $"2026-11-{day:00}", day)),
        ];
        var (handler, _, orders, expenses) = Handler(operations);
        var list = new ListOperationsHandler([orders, expenses], new FixedLaunchDate(new DateOnly(2026, 1, 1)));

        var report = (await Run(handler)).Report!;
        var page = (await list.HandleAsync(new ListOperationsQuery("2026-11-01", "2026-11-30", PageSize: "10"), TestContext.Current.CancellationToken)).Page!;

        Assert.Equal(GetCashFlowReportHandler.RecentOperationsCount, report.RecentOperations.Count);
        Assert.Equal(page.Items, report.RecentOperations);
        Assert.Equal(CashDirection.Outflow, report.RecentOperations.Single(i => i.Operation.Kind == CashOperationKind.Refund).Direction);
        Assert.Equal(CashDirection.Inflow, report.RecentOperations[0].Direction);
    }

    [Fact]
    public async Task Fewer_than_ten_operations_are_all_recent_and_none_gives_zero_cards()
    {
        // H-CF-7
        var (few, _, _, _) = Handler([CashOp(CashOperationKind.Payment, "2026-11-04", 10m), CashOp(CashOperationKind.Refund, "2026-11-05", 5m)]);
        var (none, _, _, _) = Handler();

        var fewReport = (await Run(few)).Report!;
        var noneReport = (await Run(none)).Report!;

        Assert.Equal(2, fewReport.RecentOperations.Count);
        Assert.Empty(noneReport.RecentOperations);
        Assert.Equal(new CashFlowTotals(0m, 0m, 0m), Totals(noneReport));
        Assert.All(noneReport.OutflowsByKind, item => Assert.Equal(0m, item.Amount));
    }

    [Fact]
    public async Task Clock_gives_calculation_time_and_today_in_warsaw()
    {
        // H-CF-8: 30.11 23:30 UTC to już 01.12 w Polsce.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 30, 23, 30, 0, TimeSpan.Zero));
        var (handler, _, _, _) = Handler(clock: clock);

        var report = (await Run(handler)).Report!;

        Assert.Equal(new DateTimeOffset(2026, 11, 30, 23, 30, 0, TimeSpan.Zero), report.CalculatedAtUtc);
        Assert.Equal(new DateRange(new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31)), report.Period.Range);
    }

    [Fact]
    public async Task Series_follow_the_buckets_and_sum_to_the_cards_and_breakdown_to_outflows()
    {
        // H-CF-9
        var (handler, _, _, _) = Handler(
        [
            CashOp(CashOperationKind.Payment, "2026-11-01", 1000m),
            CashOp(CashOperationKind.Refund, "2026-11-04", 100m),
            CashOp(CashOperationKind.PaidExpense, "2026-11-04", -200m),
            CashOp(CashOperationKind.PaidExpense, "2026-11-09", 500m),
            CashOp(CashOperationKind.OwnerWithdrawal, "2026-11-30", 250m),
        ]);

        var report = (await Run(handler)).Report!;

        Assert.Equal(report.Period.Buckets(), report.Series.Select(point => point.Range));
        Assert.Equal(new CashFlowTotals(1000m, 650m, 350m), Totals(report));
        Assert.Equal(report.Kpis!.Inflows.Value, report.Series.Sum(point => point.Totals.Inflows));
        Assert.Equal(report.Kpis.Outflows.Value, report.Series.Sum(point => point.Totals.Outflows));
        Assert.Equal(report.Kpis.Change.Value, report.Series.Sum(point => point.Totals.Change));
        Assert.Equal(new CashFlowTotals(0m, -100m, 100m), report.Series[3].Totals);
        Assert.Equal(
            [new KindAmount(CashOperationKind.PaidExpense, 300m), new KindAmount(CashOperationKind.OwnerWithdrawal, 250m), new KindAmount(CashOperationKind.Refund, 100m)],
            report.OutflowsByKind);
    }

    private static CashFlowTotals Totals(CashFlowReportDto report) =>
        new(report.Kpis!.Inflows.Value, report.Kpis.Outflows.Value, report.Kpis.Change.Value);
}
