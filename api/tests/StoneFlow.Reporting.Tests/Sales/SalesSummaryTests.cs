using StoneFlow.Reporting.Domain.Periods;
using StoneFlow.Reporting.Domain.Sales;
using StoneFlow.Shared.Kernel.Sales;

namespace StoneFlow.Reporting.Tests.Sales;

/// <summary>Scenariusze PRD §11 na faktach składników (S-1 … S-12).</summary>
public sealed class SalesSummaryTests
{
    private static readonly DateOnly Launch = new(2026, 9, 1);
    private static readonly IReadOnlyList<string> Channels = ["shop", "olx", "acquaintance", "phone", "other"];

    private static readonly ReportPeriod October = Month("2026-10-15");
    private static readonly ReportPeriod November = Month("2026-11-09");

    private static ReportPeriod Month(string today) =>
        ReportPeriod.Resolve(PeriodKind.CurrentMonth, null, null, null, DateOnly.Parse(today), Launch);

    private static SalesComponent Order(Guid orderId, string date, decimal value, string channel = "olx", bool counts = true) =>
        new(SalesComponentKind.Order, orderId, DateOnly.Parse(date), value, orderId, "Z/2026/1", DateOnly.Parse(date), channel, counts);

    private static SalesComponent Entry(SalesComponentKind kind, SalesComponent order, string date, decimal amount) =>
        new(kind, Guid.NewGuid(), DateOnly.Parse(date), amount, order.OrderId, order.OrderNumber, order.OrderDate, order.Channel, order.OrderCountsTowardSales);

    private static SalesSummary Of(ReportPeriod period, params SalesComponent[] components) => SalesSummary.Of(components, period, Channels);

    [Fact]
    public void S1_unpaid_order_counts_toward_sales()
    {
        var summary = Of(November, Order(Guid.NewGuid(), "2026-11-04", 1000m));

        Assert.Equal(1000m, summary.Sales);
        Assert.Equal(1, summary.OrderCount);
    }

    [Fact]
    public void S2_payment_in_another_month_does_not_move_sales()
    {
        // Wpłaty nie są składnikami: sprzedaż zależy tylko od daty zamówienia.
        var order = Order(Guid.NewGuid(), "2026-10-20", 1000m);

        Assert.Equal(1000m, Of(October, order).Sales);
        Assert.Equal(0m, Of(November, order).Sales);
    }

    [Fact]
    public void S3_corrected_order_value_counts_in_the_order_date_period()
    {
        var corrected = Order(Guid.NewGuid(), "2026-10-20", 1200m);

        Assert.Equal(1200m, Of(October, corrected).Sales);
    }

    [Fact]
    public void S4_sales_return_next_month_lowers_that_month_and_the_order_channel()
    {
        var order = Order(Guid.NewGuid(), "2026-10-20", 1000m, channel: "olx");
        var salesReturn = Entry(SalesComponentKind.SalesReturn, order, "2026-11-05", 300m);

        var october = Of(October, order, salesReturn);
        var november = Of(November, order, salesReturn);

        Assert.Equal(1000m, october.Sales);
        Assert.Equal(1, october.OrderCount);
        Assert.Equal(-300m, november.Sales);
        Assert.Equal(0, november.OrderCount);
        Assert.Equal(-300m, november.Channels.Single(c => c.Channel == "olx").Amount);
    }

    [Fact]
    public void S5_money_return_is_not_a_component()
    {
        // Źródło nie zwraca zwrotów pieniędzy: zwrot nadpłaty 200 zł nie zmienia sprzedaży.
        var summary = Of(November, Order(Guid.NewGuid(), "2026-11-04", 1000m));

        Assert.Equal(1000m, summary.Sales);
    }

    [Fact]
    public void S6_sales_correction_lowers_sales_in_the_correction_period()
    {
        var order = Order(Guid.NewGuid(), "2026-10-20", 1000m);
        var correction = Entry(SalesComponentKind.SalesCorrection, order, "2026-11-06", 500m);

        Assert.Equal(1000m, Of(October, order, correction).Sales);
        Assert.Equal(-500m, Of(November, order, correction).Sales);
    }

    [Fact]
    public void S7_cancelled_order_drops_all_its_components_and_restoring_brings_them_back()
    {
        var orderId = Guid.NewGuid();
        var cancelled = Order(orderId, "2026-10-20", 1000m, counts: false);
        var cancelledReturn = Entry(SalesComponentKind.SalesReturn, cancelled, "2026-11-05", 300m);

        Assert.Equal(0m, Of(October, cancelled, cancelledReturn).Sales);
        Assert.Equal(0, Of(October, cancelled, cancelledReturn).OrderCount);
        Assert.Equal(0m, Of(November, cancelled, cancelledReturn).Sales);

        var restored = cancelled with { OrderCountsTowardSales = true };
        var restoredReturn = cancelledReturn with { OrderCountsTowardSales = true };
        Assert.Equal(1000m, Of(October, restored, restoredReturn).Sales);
        Assert.Equal(-300m, Of(November, restored, restoredReturn).Sales);
    }

    [Fact]
    public void S8_order_from_before_launch_and_its_later_return_are_excluded()
    {
        var period = ReportPeriod.Resolve(PeriodKind.CurrentMonth, null, null, null, new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 3));
        var order = Order(Guid.NewGuid(), "2026-10-28", 1000m);
        var salesReturn = Entry(SalesComponentKind.SalesReturn, order, "2026-11-04", 300m);

        var summary = SalesSummary.Of([order, salesReturn], period, Channels);

        Assert.Equal(0m, summary.Sales);
        Assert.Equal(0, summary.OrderCount);
    }

    [Fact]
    public void S9_pending_failed_and_draft_orders_are_excluded_with_their_entries()
    {
        var excluded = Order(Guid.NewGuid(), "2026-11-04", 700m, counts: false);
        var correction = Entry(SalesComponentKind.SalesCorrection, excluded, "2026-11-05", 100m);

        var summary = Of(November, excluded, correction, Order(Guid.NewGuid(), "2026-11-06", 50m));

        Assert.Equal(50m, summary.Sales);
        Assert.Equal(1, summary.OrderCount);
    }

    [Fact]
    public void S10_channels_sum_to_sales_and_order_with_return_still_counts()
    {
        var shop = Order(Guid.NewGuid(), "2026-11-02", 400m, channel: "shop");
        var olx = Order(Guid.NewGuid(), "2026-11-03", 1000m, channel: "olx");
        var olxReturn = Entry(SalesComponentKind.SalesReturn, olx, "2026-11-04", 250m);

        var summary = Of(November, shop, olx, olxReturn);

        Assert.Equal(1150m, summary.Sales);
        Assert.Equal(2, summary.OrderCount);
        Assert.Equal(summary.Sales, summary.Channels.Sum(c => c.Amount));
        Assert.Equal(Channels, summary.Channels.Select(c => c.Channel));
        Assert.Equal(400m, summary.Channels[0].Amount);
        Assert.Equal(750m, summary.Channels[1].Amount);
    }

    [Fact]
    public void S11_buckets_sum_to_the_card()
    {
        var a = Order(Guid.NewGuid(), "2026-11-01", 100.10m);
        var b = Order(Guid.NewGuid(), "2026-11-30", 200.20m);
        var correction = Entry(SalesComponentKind.SalesCorrection, a, "2026-11-15", 0.30m);

        var summary = Of(November, a, b, correction);

        Assert.Equal(30, summary.BucketSales.Count);
        Assert.Equal(summary.Sales, summary.BucketSales.Sum());
        Assert.Equal(100.10m, summary.BucketSales[0]);
        Assert.Equal(-0.30m, summary.BucketSales[14]);
        Assert.Equal(200.20m, summary.BucketSales[29]);
    }

    [Fact]
    public void S12_zero_value_order_counts_with_zero_amount_and_channels_are_complete()
    {
        var summary = Of(November, Order(Guid.NewGuid(), "2026-11-04", 0m, channel: "phone"), Order(Guid.NewGuid(), "2026-11-04", 0.01m));

        Assert.Equal(2, summary.OrderCount);
        Assert.Equal(0.01m, summary.Sales);
        Assert.Equal(5, summary.Channels.Count);
        Assert.All(summary.Channels.Where(c => c.Channel is not "olx"), c => Assert.Equal(0m, c.Amount));
    }

    [Fact]
    public void Component_with_unknown_channel_is_a_program_error()
    {
        Assert.Throws<ArgumentException>(() => Of(November, Order(Guid.NewGuid(), "2026-11-04", 10m, channel: "allegro")));
    }

    [Fact]
    public void Signed_amounts_follow_the_kind()
    {
        var order = Order(Guid.NewGuid(), "2026-11-04", 100m);

        Assert.Equal(100m, SalesPolicy.SignedAmount(order));
        Assert.Equal(-30m, SalesPolicy.SignedAmount(Entry(SalesComponentKind.SalesReturn, order, "2026-11-05", 30m)));
        Assert.Equal(-20m, SalesPolicy.SignedAmount(Entry(SalesComponentKind.SalesCorrection, order, "2026-11-05", 20m)));
    }

    [Fact]
    public void Before_launch_period_has_no_sales()
    {
        var period = ReportPeriod.Resolve(PeriodKind.PreviousMonth, null, null, null, new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 3));

        var summary = SalesSummary.Of([Order(Guid.NewGuid(), "2026-10-20", 10m)], period, Channels);

        Assert.Equal(0m, summary.Sales);
        Assert.Empty(summary.BucketSales);
    }
}
