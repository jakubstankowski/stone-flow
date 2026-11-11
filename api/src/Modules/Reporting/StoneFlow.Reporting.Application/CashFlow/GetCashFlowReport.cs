using StoneFlow.Reporting.Application.Abstractions;
using StoneFlow.Reporting.Application.Operations;
using StoneFlow.Reporting.Application.Sales;
using StoneFlow.Reporting.Domain.CashFlow;
using StoneFlow.Reporting.Domain.Periods;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.LaunchDate;
using StoneFlow.Shared.Application.Operations;

namespace StoneFlow.Reporting.Application.CashFlow;

/// <summary>Parametry raportu przepływów z zapytania HTTP (tekst); walidacja w handlerze.</summary>
public sealed record GetCashFlowReportQuery(
    string? Period = null,
    string? Year = null,
    string? From = null,
    string? To = null) : IQuery<GetCashFlowReportResult>;

/// <summary>Trzy karty widoku „Przepływy pieniężne”.</summary>
public sealed record CashFlowKpisDto(AmountKpiDto Inflows, AmountKpiDto Outflows, AmountKpiDto Change);

/// <summary>Przedział wykresu: wpływy, wypływy i zmiana środków.</summary>
public sealed record CashFlowSeriesPointDto(DateRange Range, CashFlowTotals Totals);

/// <summary>
/// Raport przepływów. <see cref="Kpis"/> jest <c>null</c>, a listy puste, gdy okres leży w całości
/// przed uruchomieniem (brak danych ≠ zero). <see cref="ComparisonAvailable"/> = <c>false</c> oznacza
/// okres bazowy zaczynający się przed datą uruchomienia.
/// </summary>
public sealed record CashFlowReportDto(
    ReportPeriod Period,
    bool ComparisonAvailable,
    CashFlowKpisDto? Kpis,
    IReadOnlyList<CashFlowSeriesPointDto> Series,
    IReadOnlyList<KindAmount> OutflowsByKind,
    IReadOnlyList<OperationItemDto> RecentOperations,
    DateTimeOffset CalculatedAtUtc);

/// <summary>Raport, błędy parametrów (400) albo przekroczony bezpiecznik liczby operacji (422).</summary>
public sealed record GetCashFlowReportResult(CashFlowReportDto? Report, FieldErrors? Errors = null, bool TooManyRows = false);

/// <summary>
/// Raport widoku „Przepływy pieniężne” (SF-20): karty z porównaniem, szereg czasowy, podział
/// wypływów i ostatnie operacje. Te same źródła (<see cref="ICashOperationSource"/>), reguła sum
/// (<see cref="CashFlowTotals"/>), przełom uruchomienia i kolejność (<see cref="CashOperationReader"/>)
/// co lista „Operacje”, więc karty = sumy listy, a ostatnie operacje = jej pierwsza strona (FIN-05).
/// Źródła wywoływane po kolei (wspólne połączenie): okres, a przy dostępnej bazie — okres bazowy;
/// bezpiecznik osobno dla każdego odczytu. Łączenie w pamięci uzasadnia skala (D-08).
/// </summary>
public sealed class GetCashFlowReportHandler(IEnumerable<ICashOperationSource> sources, ILaunchDate launchDate, TimeProvider time)
    : IQueryHandler<GetCashFlowReportQuery, GetCashFlowReportResult>
{
    /// <summary>Liczba ostatnich operacji w raporcie (pierwsza strona listy „Operacje” przy tej wielkości).</summary>
    public const int RecentOperationsCount = 10;

    private readonly IReadOnlyList<ICashOperationSource> _sources = [.. sources];

    public async Task<GetCashFlowReportResult> HandleAsync(GetCashFlowReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new FieldErrors();
        var period = ReportPeriodInput.Validate(query.Period, query.Year, query.From, query.To, WarsawTime.Today(time), launchDate.Value, errors);
        if (period is null)
        {
            return new GetCashFlowReportResult(null, errors);
        }

        if (period.BeforeLaunch)
        {
            // Okres w całości przed uruchomieniem: źródła nie są odpytywane, brak kart (nie zera).
            return new GetCashFlowReportResult(new CashFlowReportDto(period, false, null, [], [], [], time.GetUtcNow()));
        }

        var current = await CashOperationReader.ReadAsync(_sources, period.EffectiveFrom, period.Range.To, CashOperationReader.AllKinds, cancellationToken);
        if (current is null)
        {
            return new GetCashFlowReportResult(null, TooManyRows: true);
        }

        CashFlowSummary? baseSummary = null;
        if (period.BaseAvailable)
        {
            var basePeriod = period.ForBase();
            var baseline = await CashOperationReader.ReadAsync(_sources, basePeriod.EffectiveFrom, basePeriod.Range.To, CashOperationReader.AllKinds, cancellationToken);
            if (baseline is null)
            {
                return new GetCashFlowReportResult(null, TooManyRows: true);
            }

            baseSummary = CashFlowSummary.Of(baseline.Select(Entry), basePeriod);
        }

        var summary = CashFlowSummary.Of(current.Select(Entry), period);
        var totals = summary.Totals;
        var kpis = baseSummary is { Totals: var basis }
            ? new CashFlowKpisDto(
                new AmountKpiDto(totals.Inflows, Comparison.Of(totals.Inflows, basis.Inflows)),
                new AmountKpiDto(totals.Outflows, Comparison.Of(totals.Outflows, basis.Outflows)),
                new AmountKpiDto(totals.Change, Comparison.Of(totals.Change, basis.Change)))
            : new CashFlowKpisDto(
                new AmountKpiDto(totals.Inflows, Comparison.NoBasis),
                new AmountKpiDto(totals.Outflows, Comparison.NoBasis),
                new AmountKpiDto(totals.Change, Comparison.NoBasis));

        var series = period.Buckets()
            .Select((range, index) => new CashFlowSeriesPointDto(range, summary.Buckets[index]))
            .ToList();

        var recent = CashOperationReader.Order(current, descending: true)
            .Take(RecentOperationsCount)
            .Select(operation => new OperationItemDto(operation, CashFlowPolicy.DirectionOf(operation.Kind)))
            .ToList();

        return new GetCashFlowReportResult(new CashFlowReportDto(
            period,
            baseSummary is not null,
            kpis,
            series,
            summary.OutflowsByKind,
            recent,
            time.GetUtcNow()));
    }

    private static CashFlowEntry Entry(CashOperation operation) => new(operation.Kind, operation.Date, operation.Amount);
}
