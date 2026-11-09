using System.Globalization;
using StoneFlow.Reporting.Application.Abstractions;
using StoneFlow.Reporting.Domain.CashFlow;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.LaunchDate;
using StoneFlow.Shared.Application.Operations;
using StoneFlow.Shared.Kernel.Operations;

namespace StoneFlow.Reporting.Application.Operations;

/// <summary>Parametry listy „Operacje” z zapytania HTTP (tekst); walidacja w handlerze.</summary>
public sealed record ListOperationsQuery(
    string? From = null,
    string? To = null,
    IReadOnlyList<string>? Kind = null,
    string? Direction = null,
    string? Page = null,
    string? PageSize = null) : IQuery<ListOperationsResult>;

/// <summary>Pozycja listy: operacja ze źródła i kierunek z <see cref="CashFlowPolicy"/>.</summary>
public sealed record OperationItemDto(CashOperation Operation, CashDirection Direction);

/// <summary>
/// Strona listy. <see cref="Total"/> i <see cref="Totals"/> dotyczą całego filtra (nie strony);
/// <see cref="EffectiveFrom"/> to pierwszy liczony dzień (max z „od” i daty uruchomienia).
/// </summary>
public sealed record OperationsPageDto(
    IReadOnlyList<OperationItemDto> Items,
    int Page,
    int PageSize,
    int Total,
    CashFlowTotals Totals,
    DateOnly EffectiveFrom,
    DateOnly LaunchDate);

/// <summary>Strona listy, błędy parametrów (400) albo przekroczony bezpiecznik liczby operacji (422).</summary>
public sealed record ListOperationsResult(OperationsPageDto? Page, FieldErrors? Errors = null, bool TooManyOperations = false);

/// <summary>
/// Lista operacji pieniężnych od daty uruchomienia: źródła modułów-właścicieli (Orders, Expenses)
/// wywoływane po kolei (wspólne połączenie, bez równoległych zapytań), łączenie, sumy, sortowanie i
/// stronicowanie w pamięci. To odstępstwo od stronicowania w bazie uzasadnia skala (D-08, setki
/// operacji rocznie); bezpiecznik <see cref="MaxOperations"/> jest egzekwowany limitem w SQL źródeł.
/// </summary>
public sealed class ListOperationsHandler(IEnumerable<ICashOperationSource> sources, ILaunchDate launchDate)
    : IQueryHandler<ListOperationsQuery, ListOperationsResult>
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    /// <summary>Najwięcej operacji w filtrze; więcej to <c>422 operations_range_too_large</c>.</summary>
    public const int MaxOperations = 10_000;

    private readonly IReadOnlyList<ICashOperationSource> _sources = [.. sources];

    public async Task<ListOperationsResult> HandleAsync(ListOperationsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new FieldErrors();
        var criteria = Validate(query, errors);
        if (criteria is null)
        {
            return new ListOperationsResult(null, errors);
        }

        var launch = launchDate.Value;
        var effectiveFrom = CashFlowPolicy.EffectiveFrom(criteria.From, launch);
        if (effectiveFrom > criteria.To)
        {
            // Okres w całości przed uruchomieniem: źródła nie są odpytywane.
            return new ListOperationsResult(
                new OperationsPageDto([], criteria.Page, criteria.PageSize, 0, CashFlowTotals.Of([]), effectiveFrom, launch));
        }

        var loaded = new List<CashOperation>();
        foreach (var source in _sources)
        {
            var requested = new HashSet<CashOperationKind>(criteria.Kinds.Where(source.Kinds.Contains));
            if (requested.Count == 0)
            {
                continue;
            }

            var remaining = MaxOperations + 1 - loaded.Count;
            if (remaining <= 0)
            {
                break;
            }

            loaded.AddRange(await source.ListAsync(effectiveFrom, criteria.To, requested, remaining, cancellationToken));
        }

        if (loaded.Count > MaxOperations)
        {
            return new ListOperationsResult(null, TooManyOperations: true);
        }

        var totals = CashFlowTotals.Of(loaded.Select(operation => new CashMovement(operation.Kind, operation.Amount)));
        var ordered = criteria.Descending
            ? loaded.OrderByDescending(operation => operation.Date)
            : loaded.OrderBy(operation => operation.Date);
        var skip = (long)(criteria.Page - 1) * criteria.PageSize;
        List<OperationItemDto> items = skip >= loaded.Count
            ? []
            : [.. ordered
                .ThenBy(operation => operation.Kind)
                .ThenBy(operation => operation.Id)
                .Skip((int)skip)
                .Take(criteria.PageSize)
                .Select(operation => new OperationItemDto(operation, CashFlowPolicy.DirectionOf(operation.Kind)))];

        return new ListOperationsResult(
            new OperationsPageDto(items, criteria.Page, criteria.PageSize, loaded.Count, totals, effectiveFrom, launch));
    }

    /// <summary>Domyślnie: strona 1, 25 pozycji, wszystkie rodzaje, data malejąco.</summary>
    private static Criteria? Validate(ListOperationsQuery query, FieldErrors errors)
    {
        var from = RequiredDate(query.From, "from", "Podaj datę początkową.", errors);
        var to = RequiredDate(query.To, "to", "Podaj datę końcową.", errors);
        if (from > to)
        {
            errors.Add("from", "Data początkowa nie może być późniejsza niż końcowa.");
        }

        var kinds = new HashSet<CashOperationKind>();
        foreach (var code in (query.Kind ?? []).Select(Text).OfType<string>())
        {
            if (OperationCodes.TryParse(code, out var kind))
            {
                kinds.Add(kind);
            }
            else if (!errors.Contains("kind"))
            {
                errors.Add("kind", "Nieznany rodzaj operacji.");
            }
        }

        if (kinds.Count == 0)
        {
            kinds.UnionWith(Enum.GetValues<CashOperationKind>());
        }

        var descending = true;
        switch (Text(query.Direction))
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
        if (Text(query.Page) is { } pageText
            && (!int.TryParse(pageText, NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1))
        {
            errors.Add("page", "Numer strony musi być liczbą całkowitą od 1.");
        }

        var pageSize = DefaultPageSize;
        if (Text(query.PageSize) is { } sizeText
            && (!int.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out pageSize) || pageSize is < 1 or > MaxPageSize))
        {
            errors.Add("pageSize", $"Rozmiar strony musi być liczbą od 1 do {MaxPageSize}.");
        }

        return errors.IsEmpty && from is { } validFrom && to is { } validTo
            ? new Criteria(validFrom, validTo, kinds, descending, page, pageSize)
            : null;
    }

    private static DateOnly? RequiredDate(string? text, string field, string missing, FieldErrors errors)
    {
        if (Text(text) is not { } trimmed)
        {
            errors.Add(field, missing);
            return null;
        }

        if (OperationCodes.TryParseDate(trimmed, out var date))
        {
            return date;
        }

        errors.Add(field, "Podaj datę w formacie RRRR-MM-DD.");
        return null;
    }

    private static string? Text(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private sealed record Criteria(DateOnly From, DateOnly To, IReadOnlySet<CashOperationKind> Kinds, bool Descending, int Page, int PageSize);
}
