using StoneFlow.Reporting.Application.Abstractions;
using StoneFlow.Reporting.Domain.Costs;
using StoneFlow.Reporting.Domain.Periods;
using StoneFlow.Reporting.Domain.Sales;
using StoneFlow.Shared.Application.Costs;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.LaunchDate;
using StoneFlow.Shared.Application.Sales;

namespace StoneFlow.Reporting.Application.Sales;

/// <summary>Parametry raportu sprzedaży z zapytania HTTP (tekst); walidacja w handlerze.</summary>
public sealed record GetSalesReportQuery(
    string? Period = null,
    string? Year = null,
    string? From = null,
    string? To = null) : IQuery<GetSalesReportResult>;

/// <summary>Karta z kwotą i porównaniem.</summary>
public sealed record AmountKpiDto(decimal Value, Comparison Comparison);

/// <summary>Karta z liczbą i porównaniem.</summary>
public sealed record CountKpiDto(int Value, Comparison Comparison);

/// <summary>Cztery karty widoku „Sprzedaż”.</summary>
public sealed record SalesKpisDto(AmountKpiDto Sales, AmountKpiDto Costs, AmountKpiDto Result, CountKpiDto OrderCount);

/// <summary>Przedział wykresu: sprzedaż, koszty i wynik przed wypłatami.</summary>
public sealed record SeriesPointDto(DateRange Range, decimal Sales, decimal Costs, decimal Result);

/// <summary>
/// Raport sprzedaży. <see cref="Kpis"/> jest <c>null</c>, a listy puste, gdy okres leży w całości
/// przed uruchomieniem (brak danych ≠ zero). <see cref="ComparisonAvailable"/> = <c>false</c> oznacza
/// okres bazowy zaczynający się przed datą uruchomienia.
/// </summary>
public sealed record SalesReportDto(
    ReportPeriod Period,
    bool ComparisonAvailable,
    SalesKpisDto? Kpis,
    IReadOnlyList<SeriesPointDto> Series,
    IReadOnlyList<ChannelAmount> Channels,
    IReadOnlyList<CategoryAmount> Categories,
    DateTimeOffset CalculatedAtUtc);

/// <summary>Raport, błędy parametrów (400) albo przekroczony bezpiecznik liczby wierszy (422).</summary>
public sealed record GetSalesReportResult(SalesReportDto? Report, FieldErrors? Errors = null, bool TooManyRows = false);

/// <summary>
/// Raport widoku „Sprzedaż” (SF-19): karty z porównaniem, szereg czasowy, kanały i kategorie.
/// Źródła Orders i Expenses wywoływane po kolei (wspólne połączenie, bez równoległych zapytań):
/// sprzedaż i koszty okresu, a przy dostępnej bazie — sprzedaż i koszty okresu bazowego. Reguły
/// liczy domena (<see cref="SalesSummary"/>, <see cref="CostSummary"/>, <see cref="Comparison"/>).
/// </summary>
public sealed class GetSalesReportHandler(ISalesComponentSource sales, ICostSource costs, ILaunchDate launchDate, TimeProvider time)
    : IQueryHandler<GetSalesReportQuery, GetSalesReportResult>
{
    public async Task<GetSalesReportResult> HandleAsync(GetSalesReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new FieldErrors();
        var today = WarsawTime.Today(time);
        var period = ReportPeriodInput.Validate(query.Period, query.Year, query.From, query.To, today, launchDate.Value, errors);
        if (period is null)
        {
            return new GetSalesReportResult(null, errors);
        }

        if (period.BeforeLaunch)
        {
            // Okres w całości przed uruchomieniem: źródła nie są odpytywane, brak kart (nie zera).
            return new GetSalesReportResult(new SalesReportDto(period, false, null, [], [], [], time.GetUtcNow()));
        }

        var current = await SummarizeAsync(period, cancellationToken);
        if (current is null)
        {
            return new GetSalesReportResult(null, TooManyRows: true);
        }

        Summary? baseline = null;
        if (period.BaseAvailable)
        {
            baseline = await SummarizeAsync(period.ForBase(), cancellationToken);
            if (baseline is null)
            {
                return new GetSalesReportResult(null, TooManyRows: true);
            }
        }

        var currentSales = current.Sales;
        var currentCosts = current.Costs;
        var result = SalesResult.Of(currentSales.Sales, currentCosts.Costs);
        var kpis = baseline is { } basis
            ? new SalesKpisDto(
                new AmountKpiDto(currentSales.Sales, Comparison.Of(currentSales.Sales, basis.Sales.Sales)),
                new AmountKpiDto(currentCosts.Costs, Comparison.Of(currentCosts.Costs, basis.Costs.Costs)),
                new AmountKpiDto(result, Comparison.Of(result, SalesResult.Of(basis.Sales.Sales, basis.Costs.Costs))),
                new CountKpiDto(currentSales.OrderCount, Comparison.Of(currentSales.OrderCount, basis.Sales.OrderCount)))
            : new SalesKpisDto(
                new AmountKpiDto(currentSales.Sales, Comparison.NoBasis),
                new AmountKpiDto(currentCosts.Costs, Comparison.NoBasis),
                new AmountKpiDto(result, Comparison.NoBasis),
                new CountKpiDto(currentSales.OrderCount, Comparison.NoBasis));

        var buckets = period.Buckets();
        var bucketResults = SalesResult.Buckets(currentSales.BucketSales, currentCosts.BucketCosts);
        var series = buckets
            .Select((range, index) => new SeriesPointDto(range, currentSales.BucketSales[index], currentCosts.BucketCosts[index], bucketResults[index]))
            .ToList();

        return new GetSalesReportResult(new SalesReportDto(
            period,
            baseline is not null,
            kpis,
            series,
            currentSales.Channels,
            currentCosts.Categories,
            time.GetUtcNow()));
    }

    /// <summary>Sprzedaż i koszty okresu; <c>null</c>, gdy któreś źródło przekracza bezpiecznik.</summary>
    private async Task<Summary?> SummarizeAsync(ReportPeriod period, CancellationToken cancellationToken)
    {
        var components = await SalesReportSources.SalesAsync(sales, period.Effective, cancellationToken);
        if (components is null)
        {
            return null;
        }

        var expenses = await SalesReportSources.CostsAsync(costs, period.Effective, cancellationToken);
        if (expenses is null)
        {
            return null;
        }

        return new Summary(SalesSummary.Of(components, period, sales.Channels), CostSummary.Of(expenses, period, costs.Categories));
    }

    private sealed record Summary(SalesSummary Sales, CostSummary Costs);
}
