using StoneFlow.Orders.Domain.Payments;

namespace StoneFlow.Orders.Tests.Domain;

public sealed class SalesReductionRulesTests
{
    private static EntryTotals Totals(decimal value, decimal salesReturns, decimal corrections) =>
        new(0, 0) { Value = value, SalesReturns = salesReturns, Corrections = corrections };

    [Theory]
    // wartość, zwroty sprzedaży, korekty, stary wkład, nowy wkład → available (null = przechodzi)
    [InlineData(1000, 0, 0, 0, 1000, null)] // korekta równa wartości
    [InlineData(1000, 0, 0, 0, 1000.01, 1000.00)]
    [InlineData(1000, 0, 0, 0, 1200, 1000.00)] // PLAT-05: zwrot sprzedaży 1200 przy 1000
    [InlineData(1000, 0, 500, 0, 600, 500.00)] // zmiana rodzaju zwrotu 600 na sprzedaży przy korekcie 500
    [InlineData(1000, 400, 500, 0, 100, null)]
    [InlineData(1000, 400, 500, 0, 100.01, 100.00)]
    [InlineData(1000, 0, 600, 600, 900, null)] // podwyżka własnej korekty do granicy
    [InlineData(1000, 0, 600, 600, 1000.01, 1000.00)]
    [InlineData(1000, 600, 0, 600, 0, null)] // zwrot sprzedaży zmieniony na zwrot pieniędzy
    [InlineData(300, 0, 500, 500, 400, null)] // obniżenie przy nadwyżce
    [InlineData(300, 0, 500, 500, 500, null)] // bez zmiany wkładu przy nadwyżce (np. powód)
    [InlineData(300, 0, 500, 500, 500.01, 300.00)] // podwyżka przy nadwyżce: available = V − (R − stary wkład)
    [InlineData(300, 0, 500, 0, 0.01, 0.00)] // nowa korekta przy nadwyżce
    [InlineData(0, 0, 0, 0, 0.01, 0.00)]
    public void Rejects_only_increase_above_value(
        decimal value, decimal salesReturns, decimal corrections, decimal oldContribution, decimal newContribution, double? available)
    {
        var violation = SalesReductionRules.Check(Totals(value, salesReturns, corrections), oldContribution, newContribution);

        if (available is null)
        {
            Assert.Null(violation);
        }
        else
        {
            Assert.NotNull(violation);
            Assert.Equal(EntryRuleViolationKind.SalesReductionLimitExceeded, violation.Kind);
            Assert.Equal((decimal)available.Value, violation.Available);
        }
    }

    [Theory]
    [InlineData(1000, 400, 500, 500.0)]
    [InlineData(1000, 500, 500, null)]
    [InlineData(1000, 999.99, 1000, 1000.0)]
    [InlineData(200, 300, 500, null)] // podwyżka przy nadwyżce przechodzi
    [InlineData(300, 300, 500, null)] // bez zmiany wartości
    [InlineData(1000, 0, 0, null)]
    public void Value_decrease_below_reductions_returns_minimum(decimal oldValue, decimal newValue, decimal reductions, double? minimum)
    {
        Assert.Equal(minimum is null ? null : (decimal)minimum.Value, SalesReductionRules.MinimumValueFor(oldValue, newValue, reductions));
    }

    [Fact]
    public void Summary_reports_excess_only_above_value()
    {
        Assert.Equal(new SalesReductionSummary(200m, 300m, 500m, 200m), SalesReductionRules.Summarize(300m, 200m, 300m));
        Assert.Equal(new SalesReductionSummary(200m, 800m, 1000m, null), SalesReductionRules.Summarize(1000m, 200m, 800m));
        Assert.Equal(new SalesReductionSummary(0m, 0m, 0m, null), SalesReductionRules.Summarize(0m, 0m, 0m));
    }

    [Fact]
    public void Only_sales_returns_count_as_reductions_and_corrections_do_not_change_paid()
    {
        var totals = new EntryTotals(1200m, 300m) { Value = 1000m, SalesReturns = 100m, Corrections = 500m };

        Assert.Equal(900m, totals.Paid);
        Assert.Equal(600m, totals.Reductions);
        Assert.Equal(100m, new RefundDetails(RefundKind.SalesReturn, 100m, new DateOnly(2026, 11, 5), RefundMethod.Cash, "Powód").SalesReduction);
        Assert.Equal(0m, new RefundDetails(RefundKind.MoneyReturn, 100m, new DateOnly(2026, 11, 5), RefundMethod.Cash, "Powód").SalesReduction);
        Assert.Equal(0m, TestPayments.NewRefund().SalesReduction);
    }

    [Fact]
    public void Correction_requires_positive_amount_and_reason_and_edit_lists_changes()
    {
        Assert.Throws<ArgumentException>(() => TestPayments.NewCorrection(amount: 0));
        Assert.Throws<ArgumentException>(() => SalesCorrection.Create(
            Guid.NewGuid(), TestPayments.OrderId, new SalesCorrectionDetails(10m, new DateOnly(2026, 11, 5), " "), Guid.NewGuid(), new byte[32], TestOrders.CreatedAt));

        var correction = TestPayments.NewCorrection();
        var changes = correction.Edit(new SalesCorrectionDetails(500m, new DateOnly(2026, 11, 4), "Oddany kamień"), TestOrders.Later);

        Assert.Equal([new EntryFieldChange(EntryField.Date, new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 4))], changes);
        Assert.Equal([EntryField.Amount, EntryField.Date, EntryField.Reason], correction.Snapshot().Select(field => field.Field));
    }
}
