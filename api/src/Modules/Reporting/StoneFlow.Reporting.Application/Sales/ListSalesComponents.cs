using System.Globalization;
using StoneFlow.Reporting.Application.Abstractions;
using StoneFlow.Reporting.Domain.Periods;
using StoneFlow.Reporting.Domain.Sales;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.LaunchDate;
using StoneFlow.Shared.Application.Sales;
using StoneFlow.Shared.Kernel.Sales;

namespace StoneFlow.Reporting.Application.Sales;

/// <summary>
/// Parametry zestawienia składników sprzedaży z zapytania HTTP (tekst): ten sam wybór okresu co
/// raport, filtry kanału i rodzaju, kierunek sortowania po dacie i strona.
/// </summary>
public sealed record ListSalesComponentsQuery(
    string? Period = null,
    string? Year = null,
    string? From = null,
    string? To = null,
    string? Channel = null,
    string? Kind = null,
    string? Direction = null,
    string? Page = null,
    string? PageSize = null) : IQuery<ListSalesComponentsResult>;

/// <summary>Pozycja zestawienia: składnik i jego kwota ze znakiem (<see cref="SalesPolicy.SignedAmount"/>).</summary>
public sealed record SalesComponentItemDto(SalesComponent Component, decimal SignedAmount);

/// <summary>
/// Strona zestawienia. <see cref="Total"/> i <see cref="Sum"/> dotyczą całego filtra (nie strony);
/// <see cref="Sum"/> jest <c>null</c>, gdy okres leży w całości przed uruchomieniem (brak danych ≠ zero).
/// </summary>
public sealed record SalesComponentsPageDto(
    ReportPeriod Period,
    IReadOnlyList<SalesComponentItemDto> Items,
    int Page,
    int PageSize,
    int Total,
    decimal? Sum);

/// <summary>Strona zestawienia, błędy parametrów (400) albo przekroczony bezpiecznik (422).</summary>
public sealed record ListSalesComponentsResult(SalesComponentsPageDto? Page, FieldErrors? Errors = null, bool TooManyRows = false);

/// <summary>
/// Zestawienie składników sprzedaży (SF-19): składniki wchodzące do sprzedaży w okresie
/// (<see cref="SalesPolicy.Includes"/>), filtr kanału i rodzaju, suma dla całego filtra, sortowanie po
/// dacie (remis: rodzaj, potem id) i stronicowanie w pamięci (skala D-08, bezpiecznik jak w raporcie).
/// Bez filtrów suma jest równa karcie „Sprzedaż”.
/// </summary>
public sealed class ListSalesComponentsHandler(ISalesComponentSource sales, ILaunchDate launchDate, TimeProvider time)
    : IQueryHandler<ListSalesComponentsQuery, ListSalesComponentsResult>
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public async Task<ListSalesComponentsResult> HandleAsync(ListSalesComponentsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new FieldErrors();
        var period = ReportPeriodInput.Validate(query.Period, query.Year, query.From, query.To, WarsawTime.Today(time), launchDate.Value, errors);
        var criteria = Validate(query, errors);
        if (period is null || criteria is null || !errors.IsEmpty)
        {
            return new ListSalesComponentsResult(null, errors);
        }

        if (period.BeforeLaunch)
        {
            // Okres w całości przed uruchomieniem: źródło nie jest odpytywane.
            return new ListSalesComponentsResult(new SalesComponentsPageDto(period, [], criteria.Page, criteria.PageSize, 0, null));
        }

        var components = await SalesReportSources.SalesAsync(sales, period.Effective, cancellationToken);
        if (components is null)
        {
            return new ListSalesComponentsResult(null, TooManyRows: true);
        }

        var filtered = components
            .Where(c => SalesPolicy.Includes(c, period.EffectiveFrom, period.Range.To, period.LaunchDate))
            .Where(c => criteria.Channel is null || string.Equals(c.Channel, criteria.Channel, StringComparison.Ordinal))
            .Where(c => criteria.Kind is null || c.Kind == criteria.Kind)
            .ToList();

        var sum = SalesPolicy.SumOf(filtered);
        var ordered = criteria.Descending
            ? filtered.OrderByDescending(c => c.Date)
            : filtered.OrderBy(c => c.Date);
        var skip = (long)(criteria.Page - 1) * criteria.PageSize;
        List<SalesComponentItemDto> items = skip >= filtered.Count
            ? []
            : [.. ordered
                .ThenBy(c => c.Kind)
                .ThenBy(c => c.Id)
                .Skip((int)skip)
                .Take(criteria.PageSize)
                .Select(c => new SalesComponentItemDto(c, SalesPolicy.SignedAmount(c)))];

        return new ListSalesComponentsResult(new SalesComponentsPageDto(period, items, criteria.Page, criteria.PageSize, filtered.Count, sum));
    }

    /// <summary>Domyślnie: wszystkie kanały i rodzaje, data malejąco, strona 1, 25 pozycji.</summary>
    private Criteria? Validate(ListSalesComponentsQuery query, FieldErrors errors)
    {
        var channel = ReportPeriodInput.Text(query.Channel);
        if (channel is not null && !sales.Channels.Contains(channel, StringComparer.Ordinal))
        {
            errors.Add("channel", "Nieznany kanał.");
        }

        SalesComponentKind? kind = null;
        if (ReportPeriodInput.Text(query.Kind) is { } kindText)
        {
            if (SalesReportCodes.TryParse(kindText, out SalesComponentKind parsed))
            {
                kind = parsed;
            }
            else
            {
                errors.Add("kind", "Nieznany rodzaj składnika.");
            }
        }

        var descending = true;
        switch (ReportPeriodInput.Text(query.Direction))
        {
            case null:
            case "desc":
                break;
            case "asc":
                descending = false;
                break;
            default:
                errors.Add("direction", "Kierunek sortowania to asc albo desc.");
                break;
        }

        var page = 1;
        if (ReportPeriodInput.Text(query.Page) is { } pageText
            && (!int.TryParse(pageText, NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1))
        {
            errors.Add("page", "Numer strony musi być liczbą całkowitą od 1.");
        }

        var pageSize = DefaultPageSize;
        if (ReportPeriodInput.Text(query.PageSize) is { } sizeText
            && (!int.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out pageSize) || pageSize is < 1 or > MaxPageSize))
        {
            errors.Add("pageSize", $"Rozmiar strony musi być liczbą od 1 do {MaxPageSize}.");
        }

        return errors.IsEmpty ? new Criteria(channel, kind, descending, page, pageSize) : null;
    }

    private sealed record Criteria(string? Channel, SalesComponentKind? Kind, bool Descending, int Page, int PageSize);
}
