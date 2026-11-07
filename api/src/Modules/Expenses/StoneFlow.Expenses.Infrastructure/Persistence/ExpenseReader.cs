using Microsoft.EntityFrameworkCore;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Application.GetExpense;
using StoneFlow.Expenses.Application.ListExpenses;
using StoneFlow.Expenses.Domain.Expenses;

namespace StoneFlow.Expenses.Infrastructure.Persistence;

/// <summary>
/// Odczyty wydatków: projekcje do DTO, filtrowanie, sortowanie i stronicowanie w bazie. Stan
/// wydatku liczy domena (<see cref="ExpensePayment"/>) z surowych wartości; SQL odtwarza regułę
/// stanu wyłącznie w predykacie filtra <c>state</c> (zgodność sprawdza test listy).
/// </summary>
internal sealed class ExpenseReader(ExpensesDbContext context) : IExpenseReader
{
    private const string LikeEscape = "\\";

    public async Task<ExpenseRowsPage> ListAsync(ExpenseListCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var expenses = Filter(context.Expenses.AsNoTracking(), criteria);
        var total = await expenses.CountAsync(cancellationToken);

        var skip = (long)(criteria.Page - 1) * criteria.PageSize;
        if (skip >= total)
        {
            return new ExpenseRowsPage([], total);
        }

        var rows = await Sort(expenses, criteria)
            .Skip((int)skip)
            .Take(criteria.PageSize)
            .Select(e => new ExpenseListRowDto(e.Id, e.Description, e.Category, e.Counterparty, e.CostDate, e.DueDate, e.PaidOn, e.GrossAmount))
            .ToListAsync(cancellationToken);

        return new ExpenseRowsPage(rows, total);
    }

    public async Task<ExpenseRecordDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await context.Expenses.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new ExpenseRecordDto(
                e.Id,
                e.Description,
                e.Category,
                e.Counterparty,
                e.CostDate,
                e.DueDate,
                e.PaidOn,
                e.GrossAmount,
                e.NetAmount,
                e.DocumentReference,
                e.PaymentMethod,
                e.Payer,
                e.Comment,
                e.CreatedAtUtc,
                e.UpdatedAtUtc,
                EF.Property<byte[]>(e, ExpenseConfiguration.VersionProperty)))
            .SingleOrDefaultAsync(cancellationToken);

        return record is null
            ? null
            : record with
            {
                CreatedAtUtc = DateTime.SpecifyKind(record.CreatedAtUtc, DateTimeKind.Utc),
                UpdatedAtUtc = DateTime.SpecifyKind(record.UpdatedAtUtc, DateTimeKind.Utc),
            };
    }

    public async Task<ExpenseCreationMatch?> FindByCreationRequestAsync(Guid creationRequestId, CancellationToken cancellationToken)
    {
        var row = await context.Expenses.AsNoTracking()
            .Where(e => e.CreationRequestId == creationRequestId)
            .Select(e => new { e.Id, e.CreationRequestHash, Version = EF.Property<byte[]>(e, ExpenseConfiguration.VersionProperty) })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : new ExpenseCreationMatch(row.Id, row.CreationRequestHash ?? [], row.Version);
    }

    private static IQueryable<Expense> Filter(IQueryable<Expense> expenses, ExpenseListCriteria criteria)
    {
        if (criteria.CostDateFrom is { } costFrom)
        {
            expenses = expenses.Where(e => e.CostDate >= costFrom);
        }

        if (criteria.CostDateTo is { } costTo)
        {
            expenses = expenses.Where(e => e.CostDate <= costTo);
        }

        // Zakres daty zapłaty obejmuje wyłącznie wydatki opłacone (porównanie z NULL nie jest prawdą).
        if (criteria.PaidOnFrom is { } paidFrom)
        {
            expenses = expenses.Where(e => e.PaidOn >= paidFrom);
        }

        if (criteria.PaidOnTo is { } paidTo)
        {
            expenses = expenses.Where(e => e.PaidOn <= paidTo);
        }

        if (criteria.Categories.Count > 0)
        {
            var categories = criteria.Categories;
            expenses = expenses.Where(e => categories.Contains(e.Category));
        }

        // Predykat odpowiada regule ExpensePayment.Compute.
        var today = criteria.Today;
        expenses = criteria.State switch
        {
            ExpenseStateFilter.Planned => expenses.Where(e => e.PaidOn == null),
            ExpenseStateFilter.Paid => expenses.Where(e => e.PaidOn != null),
            ExpenseStateFilter.Overdue => expenses.Where(e => e.PaidOn == null && e.DueDate != null && e.DueDate < today),
            _ => expenses,
        };

        expenses = criteria.Sign switch
        {
            ExpenseSignFilter.Positive => expenses.Where(e => e.GrossAmount > 0),
            ExpenseSignFilter.Negative => expenses.Where(e => e.GrossAmount < 0),
            _ => expenses,
        };

        if (criteria.Search is { } search)
        {
            // „Zawiera”, wielkość liter według collation bazy; %, _ i [ dosłownie.
            var pattern = "%" + EscapeLike(search) + "%";
            expenses = expenses.Where(e => EF.Functions.Like(e.Description, pattern, LikeEscape)
                || (e.Counterparty != null && EF.Functions.Like(e.Counterparty, pattern, LikeEscape))
                || (e.DocumentReference != null && EF.Functions.Like(e.DocumentReference, pattern, LikeEscape)));
        }

        return expenses;
    }

    /// <summary>Stabilna kolejność z remisem po <c>Id</c>; puste terminy i daty zapłaty zawsze na końcu.</summary>
    private static IOrderedQueryable<Expense> Sort(IQueryable<Expense> expenses, ExpenseListCriteria criteria) => (criteria.Sort, criteria.Descending) switch
    {
        (ExpenseSort.DueDate, false) => expenses.OrderBy(e => e.DueDate == null).ThenBy(e => e.DueDate).ThenBy(e => e.Id),
        (ExpenseSort.DueDate, true) => expenses.OrderBy(e => e.DueDate == null).ThenByDescending(e => e.DueDate).ThenByDescending(e => e.Id),
        (ExpenseSort.PaidOn, false) => expenses.OrderBy(e => e.PaidOn == null).ThenBy(e => e.PaidOn).ThenBy(e => e.Id),
        (ExpenseSort.PaidOn, true) => expenses.OrderBy(e => e.PaidOn == null).ThenByDescending(e => e.PaidOn).ThenByDescending(e => e.Id),
        (ExpenseSort.GrossAmount, false) => expenses.OrderBy(e => e.GrossAmount).ThenBy(e => e.Id),
        (ExpenseSort.GrossAmount, true) => expenses.OrderByDescending(e => e.GrossAmount).ThenByDescending(e => e.Id),
        (_, false) => expenses.OrderBy(e => e.CostDate).ThenBy(e => e.Id),
        (_, true) => expenses.OrderByDescending(e => e.CostDate).ThenByDescending(e => e.Id),
    };

    private static string EscapeLike(string text) =>
        text.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal)
            .Replace("[", LikeEscape + "[", StringComparison.Ordinal);
}
