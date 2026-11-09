using StoneFlow.Shared.Kernel.Operations;

namespace StoneFlow.Reporting.Domain.CashFlow;

/// <summary>Ruch pieniędzy do sum: rodzaj i kwota zapisana (ze znakiem dla wydatku).</summary>
public sealed record CashMovement(CashOperationKind Kind, decimal Amount);

/// <summary>
/// Sumy przepływów dla zbioru operacji: wpływy, wypływy i zmiana środków (wpływy minus wypływy).
/// Sumy zapisanych kwot <c>decimal(18,2)</c> według kierunku z <see cref="CashFlowPolicy"/>, bez
/// zaokrąglania i bez wartości bezwzględnej: opłacony wydatek ujemny obniża wypływy.
/// </summary>
public sealed record CashFlowTotals(decimal Inflows, decimal Outflows, decimal Change)
{
    public static CashFlowTotals Of(IEnumerable<CashMovement> movements)
    {
        ArgumentNullException.ThrowIfNull(movements);

        var inflows = 0m;
        var outflows = 0m;
        foreach (var movement in movements)
        {
            if (CashFlowPolicy.DirectionOf(movement.Kind) == CashDirection.Inflow)
            {
                inflows += movement.Amount;
            }
            else
            {
                outflows += movement.Amount;
            }
        }

        return new CashFlowTotals(inflows, outflows, inflows - outflows);
    }
}
