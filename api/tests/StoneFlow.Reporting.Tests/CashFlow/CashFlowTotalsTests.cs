using System.Globalization;
using StoneFlow.Reporting.Domain.CashFlow;
using StoneFlow.Shared.Kernel.Operations;

namespace StoneFlow.Reporting.Tests.CashFlow;

public sealed class CashFlowTotalsTests
{
    [Fact]
    public void Sums_stored_amounts_by_direction_and_change_is_inflows_minus_outflows()
    {
        var totals = CashFlowTotals.Of(
        [
            new CashMovement(CashOperationKind.Payment, 1000.00m),
            new CashMovement(CashOperationKind.Payment, 250.50m),
            new CashMovement(CashOperationKind.Refund, 200.00m),
            new CashMovement(CashOperationKind.PaidExpense, 300.25m),
            new CashMovement(CashOperationKind.OwnerWithdrawal, 2000.00m),
        ]);

        Assert.Equal(new CashFlowTotals(1250.50m, 2500.25m, -1249.75m), totals);
    }

    [Fact]
    public void Owner_withdrawal_increases_outflows_by_its_amount()
    {
        var before = CashFlowTotals.Of([new CashMovement(CashOperationKind.Payment, 1000.00m)]);
        var after = CashFlowTotals.Of([new CashMovement(CashOperationKind.Payment, 1000.00m), new CashMovement(CashOperationKind.OwnerWithdrawal, 2000.00m)]);

        Assert.Equal(2000.00m, after.Outflows - before.Outflows);
        Assert.Equal(before.Inflows, after.Inflows);
    }

    [Fact]
    public void Negative_paid_expense_lowers_outflows()
    {
        var totals = CashFlowTotals.Of(
        [
            new CashMovement(CashOperationKind.OwnerWithdrawal, 2000.00m),
            new CashMovement(CashOperationKind.PaidExpense, -50.00m),
        ]);

        Assert.Equal(1950.00m, totals.Outflows);
    }

    [Fact]
    public void Only_supplier_corrections_give_negative_outflows_and_positive_change()
    {
        var totals = CashFlowTotals.Of(
        [
            new CashMovement(CashOperationKind.PaidExpense, -50.00m),
            new CashMovement(CashOperationKind.PaidExpense, -20.10m),
        ]);

        Assert.Equal(new CashFlowTotals(0m, -70.10m, 70.10m), totals);
    }

    [Fact]
    public void No_movements_give_zero_totals()
    {
        Assert.Equal(new CashFlowTotals(0m, 0m, 0m), CashFlowTotals.Of([]));
    }

    [Fact]
    public void Keeps_scale_two_without_rounding()
    {
        var totals = CashFlowTotals.Of(
        [
            new CashMovement(CashOperationKind.Payment, 0.01m),
            new CashMovement(CashOperationKind.Payment, 0.02m),
            new CashMovement(CashOperationKind.Refund, 0.01m),
        ]);

        Assert.Equal("0.03", totals.Inflows.ToString(CultureInfo.InvariantCulture));
        Assert.Equal("0.02", totals.Change.ToString(CultureInfo.InvariantCulture));
    }
}
