using StoneFlow.Reporting.Domain.CashFlow;
using StoneFlow.Reporting.Domain.Periods;
using StoneFlow.Shared.Kernel.Operations;

namespace StoneFlow.Reporting.Tests.CashFlow;

/// <summary>CF-1 … CF-12: sumy przepływów okresu, podział wypływów i przedziały wykresu (SF-20).</summary>
public sealed class CashFlowSummaryTests
{
    private static readonly DateOnly Launch = new(2026, 11, 3);

    private static readonly ReportPeriod November = Month("2026-11-09");

    private static readonly ReportPeriod December = Month("2026-12-09");

    private static ReportPeriod Month(string today) =>
        ReportPeriod.Resolve(PeriodKind.CurrentMonth, null, null, null, DateOnly.Parse(today), Launch);

    private static CashFlowEntry Entry(CashOperationKind kind, string date, decimal amount) => new(kind, DateOnly.Parse(date), amount);

    private static decimal Outflow(CashFlowSummary summary, CashOperationKind kind) =>
        summary.OutflowsByKind.Single(item => item.Kind == kind).Amount;

    [Fact]
    public void Unpaid_order_gives_no_inflows_and_zero_totals()
    {
        // CF-1: zamówienie bez wpłaty nie jest operacją pieniężną — brak wpisów.
        var summary = CashFlowSummary.Of([], November);

        Assert.Equal(new CashFlowTotals(0m, 0m, 0m), summary.Totals);
        Assert.Equal([0m, 0m, 0m], summary.OutflowsByKind.Select(item => item.Amount));
        Assert.Equal(November.Buckets().Count, summary.Buckets.Count);
        Assert.All(summary.Buckets, bucket => Assert.Equal(new CashFlowTotals(0m, 0m, 0m), bucket));
    }

    [Fact]
    public void Payment_counts_in_the_month_of_the_payment_date()
    {
        // CF-2: wpłata 300 zł z 05.12 do zamówienia z listopada.
        CashFlowEntry[] entries = [Entry(CashOperationKind.Payment, "2026-12-05", 300m)];

        var december = CashFlowSummary.Of(entries, December);
        var november = CashFlowSummary.Of(entries, November);

        Assert.Equal(300m, december.Totals.Inflows);
        Assert.Equal(300m, december.Buckets[December.BucketIndexOf(new DateOnly(2026, 12, 5))].Inflows);
        Assert.Equal(0m, november.Totals.Inflows);
    }

    [Fact]
    public void Sales_return_and_money_return_are_both_refunds_and_outflows()
    {
        // CF-3: zwrot sprzedaży i zwrot nadpłaty to ten sam rodzaj operacji (Refund).
        var summary = CashFlowSummary.Of(
            [Entry(CashOperationKind.Refund, "2026-11-04", 120m), Entry(CashOperationKind.Refund, "2026-11-05", 30m)],
            November);

        Assert.Equal(150m, Outflow(summary, CashOperationKind.Refund));
        Assert.Equal(150m, summary.Totals.Outflows);
        Assert.Equal(-150m, summary.Totals.Change);
    }

    [Fact]
    public void Negative_paid_expense_reduces_its_item_and_the_outflows()
    {
        // CF-4: korekta od dostawcy −200 zł obok wydatku 500 zł.
        var summary = CashFlowSummary.Of(
            [Entry(CashOperationKind.PaidExpense, "2026-11-04", -200m), Entry(CashOperationKind.PaidExpense, "2026-11-06", 500m)],
            November);

        Assert.Equal(300m, Outflow(summary, CashOperationKind.PaidExpense));
        Assert.Equal(300m, summary.Totals.Outflows);
    }

    [Fact]
    public void Cancelled_paid_order_with_refund_keeps_both_operations()
    {
        // CF-5: anulowanie nie usuwa wpłaty ani zwrotu.
        var summary = CashFlowSummary.Of(
            [Entry(CashOperationKind.Payment, "2026-11-04", 800m), Entry(CashOperationKind.Refund, "2026-11-07", 800m)],
            November);

        Assert.Equal(new CashFlowTotals(800m, 800m, 0m), summary.Totals);
        Assert.Equal(800m, Outflow(summary, CashOperationKind.Refund));
    }

    [Fact]
    public void Entries_before_launch_are_skipped_and_launch_day_counts()
    {
        // CF-6: okres przecina uruchomienie (03.11).
        var summary = CashFlowSummary.Of(
            [
                Entry(CashOperationKind.Payment, "2026-11-02", 1000m),
                Entry(CashOperationKind.Payment, "2026-11-03", 100m),
                Entry(CashOperationKind.Payment, "2026-11-04", 10m),
            ],
            November);

        Assert.Equal(110m, summary.Totals.Inflows);
        Assert.Equal(100m, summary.Buckets[0].Inflows);
        Assert.Equal(new DateOnly(2026, 11, 3), November.Buckets()[0].From);
    }

    [Fact]
    public void Planned_expense_has_no_entry_and_no_outflow()
    {
        // CF-7: wydatek bez daty zapłaty nie trafia do źródła operacji — brak wpisu, brak wypływu.
        var summary = CashFlowSummary.Of([Entry(CashOperationKind.Payment, "2026-11-04", 50m)], November);

        Assert.Equal(0m, summary.Totals.Outflows);
        Assert.Equal(0m, Outflow(summary, CashOperationKind.PaidExpense));
    }

    [Fact]
    public void Owner_withdrawal_is_only_an_outflow()
    {
        // CF-8
        var summary = CashFlowSummary.Of([Entry(CashOperationKind.OwnerWithdrawal, "2026-11-10", 2000m)], November);

        Assert.Equal(new CashFlowTotals(0m, 2000m, -2000m), summary.Totals);
        Assert.Equal(2000m, Outflow(summary, CashOperationKind.OwnerWithdrawal));
        Assert.Equal(0m, Outflow(summary, CashOperationKind.PaidExpense));
        Assert.Equal(0m, Outflow(summary, CashOperationKind.Refund));
    }

    [Fact]
    public void Daily_buckets_sum_to_the_period_totals()
    {
        // CF-9: dni (bieżący miesiąc).
        var summary = CashFlowSummary.Of(
            [
                Entry(CashOperationKind.Payment, "2026-11-03", 10.10m),
                Entry(CashOperationKind.Refund, "2026-11-03", 1.01m),
                Entry(CashOperationKind.PaidExpense, "2026-11-15", -7.50m),
                Entry(CashOperationKind.OwnerWithdrawal, "2026-11-30", 40m),
            ],
            November);

        Assert.Equal(November.Buckets().Count, summary.Buckets.Count);
        AssertBucketsSumToTotals(summary);
    }

    [Fact]
    public void Monthly_buckets_are_trimmed_and_sum_to_the_period_totals()
    {
        // CF-9: miesiące (rok, pierwszy przedział przycięty do uruchomienia 15.09).
        var year = ReportPeriod.Resolve(PeriodKind.Year, 2026, null, null, new DateOnly(2026, 12, 10), new DateOnly(2026, 9, 15));
        var summary = CashFlowSummary.Of(
            [
                Entry(CashOperationKind.Payment, "2026-09-14", 999m),
                Entry(CashOperationKind.Payment, "2026-09-15", 100m),
                Entry(CashOperationKind.PaidExpense, "2026-10-31", 60m),
                Entry(CashOperationKind.Refund, "2026-12-10", 5m),
            ],
            year);

        Assert.Equal(4, summary.Buckets.Count); // 15.09–30.09, październik, listopad, 01.12–10.12
        Assert.Equal(new CashFlowTotals(100m, 0m, 100m), summary.Buckets[0]);
        Assert.Equal(new CashFlowTotals(0m, 60m, -60m), summary.Buckets[1]);
        Assert.Equal(new CashFlowTotals(0m, 5m, -5m), summary.Buckets[3]);
        AssertBucketsSumToTotals(summary);
    }

    [Fact]
    public void Breakdown_has_three_items_in_policy_order_and_sums_to_the_outflows()
    {
        // CF-10
        var summary = CashFlowSummary.Of(
            [
                Entry(CashOperationKind.Payment, "2026-11-04", 1000m),
                Entry(CashOperationKind.Refund, "2026-11-04", 15m),
                Entry(CashOperationKind.PaidExpense, "2026-11-05", 300m),
            ],
            November);

        Assert.Equal(CashFlowPolicy.OutflowKinds, summary.OutflowsByKind.Select(item => item.Kind));
        Assert.Equal(summary.Totals.Outflows, summary.OutflowsByKind.Sum(item => item.Amount));
        Assert.Equal(0m, Outflow(summary, CashOperationKind.OwnerWithdrawal));
        Assert.DoesNotContain(summary.OutflowsByKind, item => item.Kind == CashOperationKind.Payment);
    }

    [Fact]
    public void Period_before_launch_gives_zeros_three_items_and_no_buckets()
    {
        // CF-11 (rozstrzygnięcie 6): spójnie z CostSummary.
        var october = Month("2026-10-15");
        Assert.True(october.BeforeLaunch);

        var summary = CashFlowSummary.Of([Entry(CashOperationKind.Payment, "2026-10-10", 100m)], october);

        Assert.Equal(new CashFlowTotals(0m, 0m, 0m), summary.Totals);
        Assert.Equal(CashFlowPolicy.OutflowKinds, summary.OutflowsByKind.Select(item => item.Kind));
        Assert.All(summary.OutflowsByKind, item => Assert.Equal(0m, item.Amount));
        Assert.Empty(summary.Buckets);
    }

    [Fact]
    public void Entries_outside_the_effective_range_are_skipped_without_rounding()
    {
        // CF-12
        var summary = CashFlowSummary.Of(
            [
                Entry(CashOperationKind.Payment, "2026-11-02", 5m),
                Entry(CashOperationKind.Payment, "2026-12-01", 7m),
                Entry(CashOperationKind.Payment, "2026-11-30", 0.01m),
                Entry(CashOperationKind.Refund, "2026-11-03", 0.01m),
            ],
            November);

        Assert.Equal(new CashFlowTotals(0.01m, 0.01m, 0m), summary.Totals);
        AssertBucketsSumToTotals(summary);
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => CashFlowSummary.Of(null!, November));
        Assert.Throws<ArgumentNullException>(() => CashFlowSummary.Of([], null!));
    }

    private static void AssertBucketsSumToTotals(CashFlowSummary summary)
    {
        Assert.Equal(summary.Totals.Inflows, summary.Buckets.Sum(bucket => bucket.Inflows));
        Assert.Equal(summary.Totals.Outflows, summary.Buckets.Sum(bucket => bucket.Outflows));
        Assert.Equal(summary.Totals.Change, summary.Buckets.Sum(bucket => bucket.Change));
    }
}
