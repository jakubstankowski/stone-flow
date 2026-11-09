using System.Globalization;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Application.Recurring;
using StoneFlow.Expenses.Domain.Recurring;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Expenses.Application.ListRecurringExpenses;

public enum RecurringSort
{
    Description,
    StartsOn,
    GrossAmount,
}

/// <summary>
/// Zwalidowane kryteria listy; remis rozstrzyga <c>Id</c>. <see cref="Today"/> (Europe/Warsaw) służy
/// filtrowi stanu, który w SQL powtarza regułę 7.
/// </summary>
public sealed record RecurringListCriteria(
    int Page,
    int PageSize,
    RecurringSort Sort,
    bool Descending,
    RecurringDefinitionStatus? State,
    string? Search,
    DateOnly Today);

/// <summary>Pozycja listy: wiersz, stan i najbliższy nieutworzony termin (policzone przez domenę).</summary>
public sealed record RecurringListItemDto(RecurringListRowDto Row, RecurringDefinitionStatus Status, DateOnly? NextDueDate);

public sealed record RecurringListPageDto(IReadOnlyList<RecurringListItemDto> Items, int Page, int PageSize, int Total);

/// <summary>Parametry listy z zapytania HTTP (tekst); walidacja w handlerze.</summary>
public sealed record ListRecurringExpensesQuery(
    string? Page = null,
    string? PageSize = null,
    string? Sort = null,
    string? Direction = null,
    string? State = null,
    string? Search = null) : IQuery<ListRecurringExpensesResult>;

/// <summary>Strona listy albo błędy parametrów (400).</summary>
public sealed record ListRecurringExpensesResult(RecurringListPageDto? Page, FieldErrors? Errors = null);

public sealed class ListRecurringExpensesHandler(IRecurringExpenseReader reader, TimeProvider timeProvider)
    : IQueryHandler<ListRecurringExpensesQuery, ListRecurringExpensesResult>
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;
    public const int SearchMaxLength = 100;

    private static readonly Dictionary<string, RecurringSort> Sorts = new(StringComparer.Ordinal)
    {
        ["description"] = RecurringSort.Description,
        ["startsOn"] = RecurringSort.StartsOn,
        ["grossAmount"] = RecurringSort.GrossAmount,
    };

    public async Task<ListRecurringExpensesResult> HandleAsync(ListRecurringExpensesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new FieldErrors();
        var today = WarsawTime.Today(timeProvider);
        var criteria = Validate(query, today, errors);
        if (criteria is null)
        {
            return new ListRecurringExpensesResult(null, errors);
        }

        var rows = await reader.ListAsync(criteria, cancellationToken);
        var items = rows.Rows
            .Select(row => new RecurringListItemDto(
                row,
                RecurringSchedule.Status(row.LastDueDate, row.PausedOn, today),
                RecurringSchedule.NextUncreatedDueDate(row.Schedule.State, row.Schedule.Registry)))
            .ToList();
        return new ListRecurringExpensesResult(new RecurringListPageDto(items, criteria.Page, criteria.PageSize, rows.Total));
    }

    /// <summary>Domyślnie: strona 1, 25 pozycji, opis rosnąco.</summary>
    private static RecurringListCriteria? Validate(ListRecurringExpensesQuery query, DateOnly today, FieldErrors errors)
    {
        var page = 1;
        if (ExpenseInput.Text(query.Page) is { } pageText
            && (!int.TryParse(pageText, NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1))
        {
            errors.Add("page", "Numer strony musi być liczbą całkowitą od 1.");
        }

        var pageSize = DefaultPageSize;
        if (ExpenseInput.Text(query.PageSize) is { } sizeText
            && (!int.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out pageSize) || pageSize is < 1 or > MaxPageSize))
        {
            errors.Add("pageSize", $"Rozmiar strony musi być liczbą od 1 do {MaxPageSize}.");
        }

        var sort = RecurringSort.Description;
        if (ExpenseInput.Text(query.Sort) is { } sortText && !Sorts.TryGetValue(sortText, out sort))
        {
            errors.Add("sort", "Nieznane pole sortowania.");
        }

        var descending = false;
        switch (ExpenseInput.Text(query.Direction))
        {
            case null:
            case "asc":
                break;
            case "desc":
                descending = true;
                break;
            default:
                errors.Add("direction", "Kierunek sortowania to asc albo desc.");
                break;
        }

        RecurringDefinitionStatus? state = null;
        if (ExpenseInput.Text(query.State) is { } stateText)
        {
            if (RecurringCodes.TryParse(stateText, out var parsed))
            {
                state = parsed;
            }
            else
            {
                errors.Add("state", "Dozwolone wartości: active, paused, ended.");
            }
        }

        var search = ExpenseInput.Text(query.Search);
        if (search is { Length: > SearchMaxLength })
        {
            errors.Add("search", $"Wyszukiwany tekst może mieć najwyżej {SearchMaxLength} znaków.");
        }

        return errors.IsEmpty ? new RecurringListCriteria(page, pageSize, sort, descending, state, search, today) : null;
    }
}
