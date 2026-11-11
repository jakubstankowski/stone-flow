using StoneFlow.Reporting.Application.Sales;
using StoneFlow.Reporting.Domain.Periods;
using StoneFlow.Shared.Application.Costs;
using StoneFlow.Shared.Application.Sales;
using StoneFlow.Shared.Kernel.Sales;
using static StoneFlow.Reporting.Tests.Sales.ReportFacts;

namespace StoneFlow.Reporting.Tests.Sales;

/// <summary>H-1: walidacja, przełom uruchomienia, porównanie, bezpiecznik i spójność sum.</summary>
public sealed class GetSalesReportHandlerTests
{
    private const string Today = "2026-11-09";

    private static (GetSalesReportHandler Handler, SourceLog Log) Handler(
        IReadOnlyList<SalesComponentRecord>? sales = null,
        IReadOnlyList<CostRecord>? costs = null,
        string launch = "2026-01-01",
        string today = Today)
    {
        var log = new SourceLog();
        var handler = new GetSalesReportHandler(
            new FakeSalesSource(log, sales ?? []),
            new FakeCostSource(log, costs ?? []),
            new FixedLaunchDate(DateOnly.Parse(launch)),
            Clock(today));
        return (handler, log);
    }

    private static Task<GetSalesReportResult> Run(GetSalesReportHandler handler, GetSalesReportQuery query) =>
        handler.HandleAsync(query, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("week", null, null, null, "period")]
    [InlineData("year", "202", null, null, "year")]
    [InlineData("year", "20266", null, null, "year")]
    [InlineData("year", "2027", null, null, "year")]
    [InlineData("custom", null, "2026-11-01", null, "to")]
    [InlineData("custom", null, null, "2026-11-01", "from")]
    [InlineData("custom", null, "01.11.2026", "2026-11-10", "from")]
    [InlineData("custom", null, "2026-11-02", "2026-11-01", "from")]
    [InlineData("custom", null, "2026-11-01", "2027-11-10", "to")]
    public async Task Invalid_period_is_rejected_without_reading_sources(string period, string? year, string? from, string? to, string key)
    {
        var (handler, log) = Handler();

        var result = await Run(handler, new GetSalesReportQuery(period, year, from, to));

        Assert.Null(result.Report);
        Assert.True(result.Errors!.Contains(key));
        Assert.Empty(log.Calls);
    }

    [Fact]
    public async Task Messages_name_the_rule()
    {
        var (handler, _) = Handler();

        var future = await Run(handler, new GetSalesReportQuery("year", "2027"));
        var tooLate = await Run(handler, new GetSalesReportQuery("custom", null, "2026-11-01", "2027-11-10"));
        var reversed = await Run(handler, new GetSalesReportQuery("custom", null, "2026-11-02", "2026-11-01"));

        Assert.Equal(["Rok nie może być późniejszy niż bieżący."], future.Errors!.ToDictionary()["year"]);
        Assert.Equal(["Data końcowa nie może być późniejsza niż 09.11.2027 (rok od dziś)."], tooLate.Errors!.ToDictionary()["to"]);
        Assert.Equal(["Data początkowa nie może być późniejsza niż końcowa."], reversed.Errors!.ToDictionary()["from"]);
    }

    [Fact]
    public async Task Parameters_not_needed_by_the_period_are_ignored()
    {
        var (handler, _) = Handler();

        var month = await Run(handler, new GetSalesReportQuery(null, "abc", "x", "y"));
        var year = await Run(handler, new GetSalesReportQuery("year", "2026", "x", "y"));

        Assert.Equal(PeriodKind.CurrentMonth, month.Report!.Period.Kind);
        Assert.Equal(PeriodKind.Year, year.Report!.Period.Kind);
        Assert.Equal(new DateOnly(2026, 11, 9), year.Report.Period.Range.To);
    }

    [Fact]
    public async Task Period_before_launch_reads_no_sources_and_has_no_cards()
    {
        var (handler, log) = Handler(launch: "2026-11-03");

        var result = await Run(handler, new GetSalesReportQuery("custom", null, "2026-10-01", "2026-10-31"));

        var report = result.Report!;
        Assert.True(report.Period.BeforeLaunch);
        Assert.Null(report.Kpis);
        Assert.Empty(report.Series);
        Assert.Empty(report.Channels);
        Assert.Empty(report.Categories);
        Assert.False(report.ComparisonAvailable);
        Assert.Empty(log.Calls);
    }

    [Fact]
    public async Task Base_before_launch_reads_only_the_current_period_and_has_no_basis_on_every_card()
    {
        var (handler, log) = Handler(sales: [Order("2026-11-04", 100m)], launch: "2026-11-03");

        var result = await Run(handler, new GetSalesReportQuery());

        var report = result.Report!;
        Assert.False(report.ComparisonAvailable);
        Assert.Equal(
            [new SourceCall("sales", new(2026, 11, 3), new(2026, 11, 30), 10_001), new SourceCall("costs", new(2026, 11, 3), new(2026, 11, 30), 10_001)],
            log.Calls);
        Assert.All(
            [report.Kpis!.Sales.Comparison, report.Kpis.Costs.Comparison, report.Kpis.Result.Comparison, report.Kpis.OrderCount.Comparison],
            c => Assert.Equal(Comparison.NoBasis, c));
        Assert.Equal(100m, report.Kpis.Sales.Value);
    }

    [Fact]
    public async Task Available_base_reads_four_ranges_in_order_and_compares_each_card()
    {
        var (handler, log) = Handler(
            sales: [Order("2026-10-10", 1000m), Order("2026-10-11", 0m), Order("2026-11-04", 1500m), Order("2026-11-05", 100m), Order("2026-11-06", 100m)],
            costs: [Cost("2026-11-04", 300m)]);

        var report = (await Run(handler, new GetSalesReportQuery())).Report!;

        Assert.Equal(
            [
                new SourceCall("sales", new(2026, 11, 1), new(2026, 11, 30), 10_001),
                new SourceCall("costs", new(2026, 11, 1), new(2026, 11, 30), 10_001),
                new SourceCall("sales", new(2026, 10, 1), new(2026, 10, 31), 10_001),
                new SourceCall("costs", new(2026, 10, 1), new(2026, 10, 31), 10_001),
            ],
            log.Calls);
        Assert.True(report.ComparisonAvailable);
        Assert.Equal(new Comparison(ComparisonStatus.Available, 1000m, 700m, 70.0m), report.Kpis!.Sales.Comparison);
        Assert.Equal(Comparison.NoBasis, report.Kpis.Costs.Comparison); // koszty bazowe = 0
        Assert.Equal(1400m, report.Kpis.Result.Value);
        Assert.Equal(new Comparison(ComparisonStatus.Available, 1000m, 400m, 40.0m), report.Kpis.Result.Comparison);
        Assert.Equal(3, report.Kpis.OrderCount.Value);
        Assert.Equal(new Comparison(ComparisonStatus.Available, 2m, 1m, 50.0m), report.Kpis.OrderCount.Comparison);
    }

    [Fact]
    public async Task Series_buckets_sum_to_the_cards_and_lists_are_complete()
    {
        var order = Order("2026-11-04", 1000m, channel: "shop");
        var (handler, _) = Handler(
            sales: [order, Entry(SalesComponentKind.SalesReturn, order, "2026-11-20", 250.50m), Order("2026-11-30", 10.01m, channel: "olx")],
            costs: [Cost("2026-11-01", 100m), Cost("2026-11-30", -20m, "vatTax")]);

        var report = (await Run(handler, new GetSalesReportQuery("currentMonth"))).Report!;

        Assert.Equal(30, report.Series.Count);
        Assert.Equal(report.Kpis!.Sales.Value, report.Series.Sum(p => p.Sales));
        Assert.Equal(report.Kpis.Costs.Value, report.Series.Sum(p => p.Costs));
        Assert.Equal(report.Kpis.Result.Value, report.Series.Sum(p => p.Result));
        Assert.Equal(759.51m, report.Kpis.Sales.Value);
        Assert.Equal(80m, report.Kpis.Costs.Value);
        Assert.Equal(5, report.Channels.Count);
        Assert.Equal(report.Kpis.Sales.Value, report.Channels.Sum(c => c.Amount));
        Assert.Equal(7, report.Categories.Count);
        Assert.Equal(report.Kpis.Costs.Value, report.Categories.Sum(c => c.Amount));
    }

    [Fact]
    public async Task Calculated_at_comes_from_the_clock()
    {
        var (handler, _) = Handler();

        var report = (await Run(handler, new GetSalesReportQuery())).Report!;

        Assert.Equal(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero), report.CalculatedAtUtc);
    }

    [Fact]
    public async Task Exactly_ten_thousand_rows_still_give_a_report()
    {
        var (handler, _) = Handler(sales: [.. Enumerable.Range(0, 10_000).Select(_ => Order("2026-11-04", 1m))], launch: "2026-11-01");

        var result = await Run(handler, new GetSalesReportQuery());

        Assert.False(result.TooManyRows);
        Assert.Equal(10_000, result.Report!.Kpis!.OrderCount.Value);
    }

    [Fact]
    public async Task More_than_ten_thousand_sales_components_trip_the_fuse()
    {
        var (handler, _) = Handler(sales: [.. Enumerable.Range(0, 10_001).Select(_ => Order("2026-11-04", 1m))]);

        var result = await Run(handler, new GetSalesReportQuery());

        Assert.True(result.TooManyRows);
        Assert.Null(result.Report);
    }

    [Fact]
    public async Task More_than_ten_thousand_expenses_trip_the_fuse()
    {
        var (handler, _) = Handler(costs: [.. Enumerable.Range(0, 10_001).Select(_ => Cost("2026-11-04", 1m))]);

        Assert.True((await Run(handler, new GetSalesReportQuery())).TooManyRows);
    }

    [Fact]
    public async Task Fuse_also_applies_to_the_base_period()
    {
        var (handler, log) = Handler(costs: [.. Enumerable.Range(0, 10_001).Select(_ => Cost("2026-10-04", 1m))]);

        var result = await Run(handler, new GetSalesReportQuery());

        Assert.True(result.TooManyRows);
        Assert.Equal(4, log.Calls.Count);
    }
}
