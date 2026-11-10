using StoneFlow.Reporting.Domain.Costs;
using StoneFlow.Reporting.Domain.Periods;
using StoneFlow.Reporting.Domain.Sales;
using StoneFlow.Shared.Application.Costs;
using StoneFlow.Shared.Application.Sales;

namespace StoneFlow.Reporting.Application.Sales;

/// <summary>
/// Odczyt źródeł raportu sprzedaży z bezpiecznikiem (SF-19, jak SF-17): każde źródło dostaje limit
/// <see cref="MaxRows"/> + 1 wierszy (SQL <c>TOP</c>), a więcej niż <see cref="MaxRows"/> to
/// <c>null</c> (<c>422 report_range_too_large</c>). Łączenie i sumy w pamięci uzasadnia skala (D-08).
/// </summary>
internal static class SalesReportSources
{
    /// <summary>Najwięcej składników sprzedaży albo wydatków w zakresie.</summary>
    public const int MaxRows = 10_000;

    public static async Task<IReadOnlyList<SalesComponent>?> SalesAsync(ISalesComponentSource source, DateRange range, CancellationToken cancellationToken)
    {
        var records = await source.ListAsync(range.From, range.To, MaxRows + 1, cancellationToken);
        return records.Count > MaxRows
            ? null
            : [.. records.Select(r => new SalesComponent(r.Kind, r.Id, r.Date, r.Amount, r.OrderId, r.OrderNumber, r.OrderDate, r.Channel, r.OrderCountsTowardSales))];
    }

    public static async Task<IReadOnlyList<Cost>?> CostsAsync(ICostSource source, DateRange range, CancellationToken cancellationToken)
    {
        var records = await source.ListAsync(range.From, range.To, MaxRows + 1, cancellationToken);
        return records.Count > MaxRows
            ? null
            : [.. records.Select(r => new Cost(r.Id, r.CostDate, r.GrossAmount, r.Category))];
    }
}
