using StoneFlow.Reporting.Domain.Periods;
using StoneFlow.Shared.Kernel.Operations;

namespace StoneFlow.Reporting.Domain.CashFlow;

/// <summary>
/// Fakt operacji pieniężnej do raportu przepływów: rodzaj, data operacji pieniężnej (wpłaty,
/// zwrotu, zapłaty wydatku albo wypłaty) i kwota zapisana (ze znakiem dla wydatku).
/// </summary>
public sealed record CashFlowEntry(CashOperationKind Kind, DateOnly Date, decimal Amount);

/// <summary>Suma kwot jednego rodzaju operacji.</summary>
public sealed record KindAmount(CashOperationKind Kind, decimal Amount);

/// <summary>
/// Przepływy pieniężne okresu (SF-20, PRD §8 „Przepływy pieniężne”): wpływy, wypływy i zmiana
/// środków operacji z datą w [efektywny początek, to], podział wypływów według rodzaju i sumy w
/// przedziałach wykresu. Sumy liczy <see cref="CashFlowTotals"/> — ta sama reguła co sumy listy
/// „Operacje” — bez zaokrąglania i bez wartości bezwzględnej (opłacony wydatek ujemny obniża swoją
/// pozycję i wypływy). Suma <see cref="OutflowsByKind"/> = <c>Totals.Outflows</c>, suma
/// <see cref="Buckets"/> = <see cref="Totals"/>.
/// </summary>
/// <param name="Totals">Sumy okresu.</param>
/// <param name="OutflowsByKind">Zawsze trzy pozycje w kolejności <see cref="CashFlowPolicy.OutflowKinds"/>.</param>
/// <param name="Buckets">Sumy w przedziałach <see cref="ReportPeriod.Buckets"/> (po indeksie).</param>
public sealed record CashFlowSummary(CashFlowTotals Totals, IReadOnlyList<KindAmount> OutflowsByKind, IReadOnlyList<CashFlowTotals> Buckets)
{
    /// <summary>
    /// Okres w całości przed uruchomieniem daje zera, trzy pozycje podziału z zerem i brak
    /// przedziałów (spójnie z <c>CostSummary</c>); wpisy są wtedy ignorowane. Wpisy spoza liczonego
    /// zakresu są pomijane.
    /// </summary>
    public static CashFlowSummary Of(IEnumerable<CashFlowEntry> entries, ReportPeriod period)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(period);

        if (period.BeforeLaunch)
        {
            return new CashFlowSummary(
                CashFlowTotals.Of([]),
                [.. CashFlowPolicy.OutflowKinds.Select(kind => new KindAmount(kind, 0m))],
                []);
        }

        var effective = period.Effective;
        var counted = entries.Where(entry => effective.Contains(entry.Date)).ToList();

        var totals = CashFlowTotals.Of(counted.Select(Movement));
        var byKind = CashFlowPolicy.OutflowKinds
            .Select(kind => new KindAmount(kind, counted.Where(entry => entry.Kind == kind).Sum(entry => entry.Amount)))
            .ToList();
        var byBucket = counted.ToLookup(entry => period.BucketIndexOf(entry.Date));
        var buckets = period.Buckets()
            .Select((_, index) => CashFlowTotals.Of(byBucket[index].Select(Movement)))
            .ToList();

        return new CashFlowSummary(totals, byKind, buckets);
    }

    private static CashMovement Movement(CashFlowEntry entry) => new(entry.Kind, entry.Amount);
}
