using StoneFlow.Orders.Domain.Payments;

namespace StoneFlow.Orders.Tests.Domain;

public sealed class PaymentSummaryTests
{
    public static TheoryData<decimal, bool, decimal, decimal, decimal, PaymentState?, decimal?, decimal?, decimal?> Cases => new()
    {
        // wartość, anulowane, wpłaty, zwroty → Zapłacono, stan, Do zapłaty, Nadpłata, Do zwrotu
        { 1000m, false, 0m, 0m, 0m, PaymentState.Unpaid, 1000m, null, null },
        { 1000m, false, 300m, 0m, 300m, PaymentState.PartiallyPaid, 700m, null, null },
        { 1000m, false, 999.99m, 0m, 999.99m, PaymentState.PartiallyPaid, 0.01m, null, null },
        { 1000m, false, 1000m, 0m, 1000m, PaymentState.Paid, null, null, null },
        { 1000m, false, 1200m, 0m, 1200m, PaymentState.Paid, null, 200m, null },
        { 1000m, false, 1200m, 200m, 1000m, PaymentState.Paid, null, null, null },
        { 1000m, false, 500m, 500m, 0m, PaymentState.Unpaid, 1000m, null, null },
        { 0m, false, 0m, 0m, 0m, PaymentState.Paid, null, null, null },
        { 0m, false, 50m, 0m, 50m, PaymentState.Paid, null, 50m, null },
        { 0m, false, 50m, 50m, 0m, PaymentState.Paid, null, null, null },
        { 1000m, true, 0m, 0m, 0m, null, null, null, null },
        { 1000m, true, 500m, 0m, 500m, null, null, null, 500m },
        { 1000m, true, 1200m, 0m, 1200m, null, null, null, 1200m },
        { 1000m, true, 500m, 500m, 0m, null, null, null, null },
        { 0m, true, 50m, 0m, 50m, null, null, null, 50m },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Computes_paid_state_and_warnings(
        decimal value,
        bool cancelled,
        decimal payments,
        decimal refunds,
        decimal paid,
        PaymentState? state,
        decimal? amountDue,
        decimal? overpayment,
        decimal? toRefund)
    {
        var summary = PaymentSummary.Compute(value, cancelled, payments, refunds);

        Assert.Equal(new PaymentSummary(paid, state, amountDue, overpayment, toRefund), summary);
    }

    [Fact]
    public void Totals_replace_amounts_for_add_edit_and_delete()
    {
        var totals = new EntryTotals(1000m, 300m);

        Assert.Equal(700m, totals.Paid);
        Assert.Equal(1200m, totals.ReplacingPayment(0, 500m).Paid);
        Assert.Equal(-200m, totals.ReplacingPayment(1000m, 100m).Paid);
        Assert.Equal(-300m, totals.ReplacingPayment(1000m, 0).Paid);
        Assert.Equal(1000m, totals.ReplacingRefund(300m, 0).Paid);
        Assert.Equal(500m, totals.ReplacingRefund(300m, 500m).Paid);
    }

    [Theory]
    [InlineData(1000, 300, 1000, 100, true)]
    [InlineData(1000, 300, 1000, 300, false)]
    [InlineData(1000, 300, 1000, 0, true)]
    [InlineData(1300, 300, 1000, 0, false)]
    public void Payment_change_cannot_make_paid_negative(decimal payments, decimal refunds, decimal oldAmount, decimal newAmount, bool rejected)
    {
        var violation = PaidRules.CheckPaymentChange(new EntryTotals(payments, refunds), oldAmount, newAmount);

        Assert.Equal(rejected, violation is not null);
        if (violation is not null)
        {
            Assert.Equal(PaidRuleViolationKind.PaidWouldBeNegative, violation.Kind);
            Assert.Null(violation.Available);
        }
    }

    [Theory]
    [InlineData(1000, 0, 0, 1000, null)]
    [InlineData(1000, 0, 0, 1200, 1000.00)]
    [InlineData(1000, 600, 0, 600, 400.00)]
    [InlineData(1000, 600, 600, 1000, null)]
    [InlineData(1000, 600, 600, 1000.01, 1000.00)]
    [InlineData(0, 0, 0, 0.01, 0.00)]
    public void Refund_cannot_exceed_paid_and_reports_available(
        decimal payments, decimal refunds, decimal oldAmount, decimal newAmount, double? available)
    {
        var violation = PaidRules.CheckRefund(new EntryTotals(payments, refunds), oldAmount, newAmount);

        if (available is null)
        {
            Assert.Null(violation);
        }
        else
        {
            Assert.NotNull(violation);
            Assert.Equal(PaidRuleViolationKind.RefundExceedsPaid, violation.Kind);
            Assert.Equal((decimal)available.Value, violation.Available);
        }
    }
}
