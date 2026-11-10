using StoneFlow.Reporting.Application.CashFlow;
using StoneFlow.Reporting.Application.Operations;

namespace StoneFlow.Reporting.Api.Endpoints;

// Kontrakt HTTP raportu przepływów (SF-20): kwoty jako tekst dziesiętny z kropką i opcjonalnym
// minusem ("-1234.50"), procent z jednym miejscem po kropce ("12.5"), daty yyyy-MM-dd, rodzaje
// operacji jako kody (OperationCodes). Okres, nagłówek porównania, karty i pozycje operacji mają
// kształt raportu sprzedaży (SF-19) i listy „Operacje” (SF-17). Bez pola salda.

public sealed record CashFlowKpisResponse(AmountKpiResponse Inflows, AmountKpiResponse Outflows, AmountKpiResponse Change);

public sealed record CashFlowSeriesPointResponse(string From, string To, string Inflows, string Outflows, string Change);

/// <summary>Pozycja podziału wypływów: <c>kind</c> = <c>expense</c>, <c>withdrawal</c> albo <c>refund</c>.</summary>
public sealed record OutflowKindAmountResponse(string Kind, string Amount);

public sealed record CashFlowReportResponse(
    SalesReportPeriodResponse Period,
    SalesReportComparisonResponse Comparison,
    CashFlowKpisResponse? Kpis,
    IReadOnlyList<CashFlowSeriesPointResponse> Series,
    IReadOnlyList<OutflowKindAmountResponse> OutflowsByKind,
    IReadOnlyList<OperationItemResponse> RecentOperations,
    DateTimeOffset CalculatedAtUtc);

internal static class CashFlowReportContracts
{
    public static CashFlowReportResponse ToResponse(CashFlowReportDto report) => new(
        SalesReportContracts.ToResponse(report.Period),
        SalesReportContracts.ComparisonOf(report.Period, report.ComparisonAvailable),
        report.Kpis is { } kpis
            ? new CashFlowKpisResponse(
                SalesReportContracts.ToResponse(kpis.Inflows),
                SalesReportContracts.ToResponse(kpis.Outflows),
                SalesReportContracts.ToResponse(kpis.Change))
            : null,
        [.. report.Series.Select(point => new CashFlowSeriesPointResponse(
            OperationCodes.FormatDate(point.Range.From),
            OperationCodes.FormatDate(point.Range.To),
            OperationCodes.FormatAmount(point.Totals.Inflows),
            OperationCodes.FormatAmount(point.Totals.Outflows),
            OperationCodes.FormatAmount(point.Totals.Change)))],
        [.. report.OutflowsByKind.Select(item => new OutflowKindAmountResponse(OperationCodes.Of(item.Kind), OperationCodes.FormatAmount(item.Amount)))],
        [.. report.RecentOperations.Select(item => OperationContracts.ToResponse(item))],
        report.CalculatedAtUtc.ToUniversalTime());
}
