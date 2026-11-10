using System.Globalization;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Domain.Expenses;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Expenses.Application.ListExpenses;

/// <summary>Wiersz listy z bazy: surowe wartości, bez stanu.</summary>
public sealed record ExpenseListRowDto(
    Guid Id,
    string Description,
    ExpenseCategory Category,
    string? Counterparty,
    DateOnly CostDate,
    DateOnly? DueDate,
    DateOnly? PaidOn,
    decimal GrossAmount,
    Guid? RecurringDefinitionId);

/// <summary>
/// Jedna strona wierszy z bazy; <see cref="Total"/> to liczba, a <see cref="GrossTotal"/> suma kwot
/// brutto wszystkich wydatków spełniających filtry (nie strony, SF-19).
/// </summary>
public sealed record ExpenseRowsPage(IReadOnlyList<ExpenseListRowDto> Rows, int Total, decimal GrossTotal);

/// <summary>Pozycja listy: wiersz i stan policzony tą samą regułą domeny co w szczegółach.</summary>
public sealed record ExpenseListItemDto(ExpenseListRowDto Row, ExpensePaymentInfo Payment);

/// <summary>Strona listy; <see cref="Total"/> i <see cref="GrossTotal"/> dotyczą całego filtra.</summary>
public sealed record ExpenseListPageDto(IReadOnlyList<ExpenseListItemDto> Items, int Page, int PageSize, int Total, decimal GrossTotal);

public enum ExpenseSort
{
    CostDate,
    DueDate,
    PaidOn,
    GrossAmount,
}

/// <summary>Filtr stanu: <c>Overdue</c> to podzbiór planowanych (termin przed dzisiaj).</summary>
public enum ExpenseStateFilter
{
    Planned,
    Paid,
    Overdue,
}

/// <summary>Filtr znaku kwoty brutto.</summary>
public enum ExpenseSignFilter
{
    Positive,
    Negative,
}

/// <summary>
/// Zwalidowane kryteria listy; remis rozstrzyga <c>Id</c>. <see cref="Today"/> (Europe/Warsaw)
/// służy filtrowi „po terminie”. Zakresy dat mogą być jednostronne.
/// </summary>
public sealed record ExpenseListCriteria(
    int Page,
    int PageSize,
    ExpenseSort Sort,
    bool Descending,
    DateOnly? CostDateFrom,
    DateOnly? CostDateTo,
    DateOnly? PaidOnFrom,
    DateOnly? PaidOnTo,
    IReadOnlyList<ExpenseCategory> Categories,
    ExpenseStateFilter? State,
    ExpenseSignFilter? Sign,
    string? Search,
    Guid? RecurringDefinitionId,
    DateOnly Today);

/// <summary>Parametry listy z zapytania HTTP (tekst); walidacja w handlerze.</summary>
public sealed record ListExpensesQuery(
    string? Page = null,
    string? PageSize = null,
    string? Sort = null,
    string? Direction = null,
    string? CostDateFrom = null,
    string? CostDateTo = null,
    string? PaidOnFrom = null,
    string? PaidOnTo = null,
    IReadOnlyList<string>? Category = null,
    string? State = null,
    string? Sign = null,
    string? Search = null,
    string? RecurringDefinitionId = null) : IQuery<ListExpensesResult>;

/// <summary>Strona listy albo błędy parametrów (400).</summary>
public sealed record ListExpensesResult(ExpenseListPageDto? Page, FieldErrors? Errors = null);

public sealed class ListExpensesHandler(IExpenseReader reader, TimeProvider timeProvider) : IQueryHandler<ListExpensesQuery, ListExpensesResult>
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;
    public const int SearchMaxLength = 100;

    private const string DateRangeMessage = "Data końcowa nie może być wcześniejsza niż początkowa.";

    private static readonly Dictionary<string, ExpenseSort> Sorts = new(StringComparer.Ordinal)
    {
        ["costDate"] = ExpenseSort.CostDate,
        ["dueDate"] = ExpenseSort.DueDate,
        ["paidOn"] = ExpenseSort.PaidOn,
        ["grossAmount"] = ExpenseSort.GrossAmount,
    };

    private static readonly Dictionary<string, ExpenseStateFilter> States = new(StringComparer.Ordinal)
    {
        ["planned"] = ExpenseStateFilter.Planned,
        ["paid"] = ExpenseStateFilter.Paid,
        ["overdue"] = ExpenseStateFilter.Overdue,
    };

    private static readonly Dictionary<string, ExpenseSignFilter> Signs = new(StringComparer.Ordinal)
    {
        ["positive"] = ExpenseSignFilter.Positive,
        ["negative"] = ExpenseSignFilter.Negative,
    };

    public async Task<ListExpensesResult> HandleAsync(ListExpensesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new FieldErrors();
        var today = WarsawTime.Today(timeProvider);
        var criteria = Validate(query, today, errors);
        if (criteria is null)
        {
            return new ListExpensesResult(null, errors);
        }

        var rows = await reader.ListAsync(criteria, cancellationToken);
        var items = rows.Rows
            .Select(row => new ExpenseListItemDto(row, ExpensePayment.Compute(row.PaidOn, row.DueDate, today)))
            .ToList();
        return new ListExpensesResult(new ExpenseListPageDto(items, criteria.Page, criteria.PageSize, rows.Total, rows.GrossTotal));
    }

    /// <summary>Domyślnie: strona 1, 25 pozycji, data kosztu malejąco. Podany <c>sort</c> bez <c>direction</c> sortuje rosnąco.</summary>
    private static ExpenseListCriteria? Validate(ListExpensesQuery query, DateOnly today, FieldErrors errors)
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

        var sort = ExpenseSort.CostDate;
        var sortText = ExpenseInput.Text(query.Sort);
        if (sortText is not null && !Sorts.TryGetValue(sortText, out sort))
        {
            errors.Add("sort", "Nieznane pole sortowania.");
        }

        var descending = sortText is null;
        switch (ExpenseInput.Text(query.Direction))
        {
            case null:
                break;
            case "asc":
                descending = false;
                break;
            case "desc":
                descending = true;
                break;
            default:
                errors.Add("direction", "Kierunek sortowania to asc albo desc.");
                break;
        }

        var costDateFrom = ParseDate(query.CostDateFrom, "costDateFrom", errors);
        var costDateTo = ParseDate(query.CostDateTo, "costDateTo", errors);
        if (costDateFrom > costDateTo)
        {
            errors.Add("costDateTo", DateRangeMessage);
        }

        var paidOnFrom = ParseDate(query.PaidOnFrom, "paidOnFrom", errors);
        var paidOnTo = ParseDate(query.PaidOnTo, "paidOnTo", errors);
        if (paidOnFrom > paidOnTo)
        {
            errors.Add("paidOnTo", DateRangeMessage);
        }

        var categories = new List<ExpenseCategory>();
        foreach (var code in (query.Category ?? []).Select(ExpenseInput.Text).OfType<string>())
        {
            if (ExpenseCodes.TryParse(code, out ExpenseCategory category))
            {
                categories.Add(category);
            }
            else if (!errors.Contains("category"))
            {
                errors.Add("category", ExpenseMessages.CategoryUnknown);
            }
        }

        ExpenseStateFilter? state = null;
        if (ExpenseInput.Text(query.State) is { } stateText)
        {
            if (States.TryGetValue(stateText, out var parsed))
            {
                state = parsed;
            }
            else
            {
                errors.Add("state", "Dozwolone wartości: planned, paid, overdue.");
            }
        }

        ExpenseSignFilter? sign = null;
        if (ExpenseInput.Text(query.Sign) is { } signText)
        {
            if (Signs.TryGetValue(signText, out var parsed))
            {
                sign = parsed;
            }
            else
            {
                errors.Add("sign", "Dozwolone wartości: positive, negative.");
            }
        }

        var search = ExpenseInput.Text(query.Search);
        if (search is { Length: > SearchMaxLength })
        {
            errors.Add("search", $"Wyszukiwany tekst może mieć najwyżej {SearchMaxLength} znaków.");
        }

        Guid? recurringDefinitionId = null;
        if (ExpenseInput.Text(query.RecurringDefinitionId) is { } definitionText)
        {
            if (Guid.TryParse(definitionText, out var parsed))
            {
                recurringDefinitionId = parsed;
            }
            else
            {
                errors.Add("recurringDefinitionId", "Niepoprawny identyfikator wydatku cyklicznego.");
            }
        }

        return errors.IsEmpty
            ? new ExpenseListCriteria(
                page,
                pageSize,
                sort,
                descending,
                costDateFrom,
                costDateTo,
                paidOnFrom,
                paidOnTo,
                [.. categories.Distinct()],
                state,
                sign,
                search,
                recurringDefinitionId,
                today)
            : null;
    }

    private static DateOnly? ParseDate(string? text, string field, FieldErrors errors)
    {
        if (ExpenseInput.Text(text) is not { } trimmed)
        {
            return null;
        }

        if (ExpenseInput.TryParseDate(trimmed, out var date))
        {
            return date;
        }

        errors.Add(field, "Podaj datę w formacie RRRR-MM-DD.");
        return null;
    }
}
