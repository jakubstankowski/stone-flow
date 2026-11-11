using System.Globalization;
using StoneFlow.Reporting.Domain.CashFlow;
using StoneFlow.Shared.Kernel.Operations;

namespace StoneFlow.Reporting.Tests.CashFlow;

public sealed class CashFlowPolicyTests
{
    private static readonly DateOnly Launch = new(2026, 11, 3);

    [Theory]
    [InlineData(CashOperationKind.Payment, CashDirection.Inflow)]
    [InlineData(CashOperationKind.Refund, CashDirection.Outflow)]
    [InlineData(CashOperationKind.PaidExpense, CashDirection.Outflow)]
    [InlineData(CashOperationKind.OwnerWithdrawal, CashDirection.Outflow)]
    public void Only_a_payment_is_an_inflow(CashOperationKind kind, CashDirection expected)
    {
        Assert.Equal(expected, CashFlowPolicy.DirectionOf(kind));
    }

    [Fact]
    public void Every_kind_has_a_direction()
    {
        Assert.All(Enum.GetValues<CashOperationKind>(), kind => Assert.True(Enum.IsDefined(CashFlowPolicy.DirectionOf(kind))));
    }

    [Theory]
    [InlineData("2026-11-01", "2026-11-03")]
    [InlineData("2026-11-03", "2026-11-03")]
    [InlineData("2026-11-04", "2026-11-04")]
    public void Effective_start_is_the_later_of_the_filter_start_and_the_launch_date(string from, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected, CultureInfo.InvariantCulture), CashFlowPolicy.EffectiveFrom(DateOnly.Parse(from, CultureInfo.InvariantCulture), Launch));
    }

    [Fact]
    public void Period_entirely_before_launch_starts_after_its_end()
    {
        var to = new DateOnly(2026, 10, 31);

        var effectiveFrom = CashFlowPolicy.EffectiveFrom(new DateOnly(2026, 10, 1), Launch);

        Assert.Equal(Launch, effectiveFrom);
        Assert.True(effectiveFrom > to);
    }

    [Fact]
    public void Outflow_kinds_follow_the_breakdown_order_and_cover_every_outflow()
    {
        Assert.Equal(
            [CashOperationKind.PaidExpense, CashOperationKind.OwnerWithdrawal, CashOperationKind.Refund],
            CashFlowPolicy.OutflowKinds);
        Assert.All(CashFlowPolicy.OutflowKinds, kind => Assert.Equal(CashDirection.Outflow, CashFlowPolicy.DirectionOf(kind)));
        Assert.Equal(
            Enum.GetValues<CashOperationKind>().Where(kind => CashFlowPolicy.DirectionOf(kind) == CashDirection.Outflow).Order(),
            CashFlowPolicy.OutflowKinds.Order());
    }
}
