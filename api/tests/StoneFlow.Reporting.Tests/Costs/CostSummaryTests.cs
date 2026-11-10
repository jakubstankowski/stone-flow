using StoneFlow.Reporting.Domain.Costs;
using StoneFlow.Reporting.Domain.Periods;
using StoneFlow.Reporting.Domain.Sales;

namespace StoneFlow.Reporting.Tests.Costs;

/// <summary>C-1: koszty według daty kosztu, opłacone i nieopłacone, ze znakiem.</summary>
public sealed class CostSummaryTests
{
    private static readonly IReadOnlyList<string> Categories =
        ["materialPurchase", "cutting", "hdsTransport", "marketing", "otherOperations", "vatTax", "ppeTax"];

    private static readonly ReportPeriod November =
        ReportPeriod.Resolve(PeriodKind.CurrentMonth, null, null, null, new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 3));

    private static Cost Cost(string date, decimal amount, string category = "materialPurchase") =>
        new(Guid.NewGuid(), DateOnly.Parse(date), amount, category);

    [Fact]
    public void Planned_negative_and_pre_launch_expenses_follow_the_cost_date()
    {
        var summary = CostSummary.Of(
            [
                Cost("2026-11-05", 500m), // nieopłacony (planowany): w kosztach według daty kosztu
                Cost("2026-11-20", 100m, "vatTax"), // przyszła data w bieżącym miesiącu
                Cost("2026-11-06", -200m, "hdsTransport"), // korekta od dostawcy
                Cost("2026-11-02", 999m), // sprzed uruchomienia (opłacony po nim): poza kosztami
            ],
            November,
            Categories);

        Assert.Equal(400m, summary.Costs);
        Assert.Equal(-200m, summary.Categories.Single(c => c.Category == "hdsTransport").Amount);
        Assert.Equal(100m, summary.Categories.Single(c => c.Category == "vatTax").Amount);
        Assert.Equal(500m, summary.Categories.Single(c => c.Category == "materialPurchase").Amount);
    }

    [Fact]
    public void Category_list_is_complete_in_dictionary_order_including_vat_and_ppe()
    {
        var summary = CostSummary.Of([Cost("2026-11-05", 10m, "ppeTax")], November, Categories);

        Assert.Equal(Categories, summary.Categories.Select(c => c.Category));
        Assert.Equal(10m, summary.Categories.Single(c => c.Category == "ppeTax").Amount);
        Assert.Equal(summary.Costs, summary.Categories.Sum(c => c.Amount));
    }

    [Fact]
    public void Buckets_sum_to_the_card_and_result_is_sales_minus_costs()
    {
        var summary = CostSummary.Of([Cost("2026-11-03", 10.10m), Cost("2026-11-30", 5.05m)], November, Categories);

        Assert.Equal(28, summary.BucketCosts.Count); // 03.11–30.11
        Assert.Equal(summary.Costs, summary.BucketCosts.Sum());
        Assert.Equal(10.10m, summary.BucketCosts[0]);

        Assert.Equal(-115.15m, SalesResult.Of(-100m, 15.15m));
        var sales = new decimal[28];
        sales[0] = 50m;
        var buckets = SalesResult.Buckets(sales, summary.BucketCosts);
        Assert.Equal(39.90m, buckets[0]);
        Assert.Equal(-5.05m, buckets[27]);
    }

    [Fact]
    public void Expense_with_unknown_category_is_a_program_error()
    {
        Assert.Throws<ArgumentException>(() => CostSummary.Of([Cost("2026-11-05", 1m, "fuel")], November, Categories));
    }
}
