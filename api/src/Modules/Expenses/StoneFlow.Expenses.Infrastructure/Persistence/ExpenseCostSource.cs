using Microsoft.EntityFrameworkCore;
using StoneFlow.Expenses.Application;
using StoneFlow.Expenses.Domain.Expenses;
using StoneFlow.Shared.Application.Costs;

namespace StoneFlow.Expenses.Infrastructure.Persistence;

/// <summary>
/// Koszty modułu Expenses (SF-19): wszystkie wydatki z datą kosztu w zakresie — opłacone i
/// nieopłacone, także wystąpienia cykliczne — z kwotą brutto ze znakiem. Ten sam zbiór co lista
/// wydatków z filtrem daty kosztu (wydatki nie mają miękkiego usuwania). Wypłaty właścicieli nie są
/// czytane. Projekcja w SQL z filtrem zakresu i limitem.
/// </summary>
internal sealed class ExpenseCostSource(ExpensesDbContext context) : ICostSource
{
    public IReadOnlyList<string> Categories { get; } = [.. Enum.GetValues<ExpenseCategory>().Select(ExpenseCodes.Of)];

    public async Task<IReadOnlyList<CostRecord>> ListAsync(DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken)
    {
        var expenses = await context.Expenses.AsNoTracking()
            .Where(e => e.CostDate >= from && e.CostDate <= to)
            .OrderBy(e => e.CostDate)
            .ThenBy(e => e.Id)
            .Take(limit)
            .Select(e => new { e.Id, e.CostDate, e.GrossAmount, e.Category })
            .ToListAsync(cancellationToken);

        return [.. expenses.Select(e => new CostRecord(e.Id, e.CostDate, e.GrossAmount, ExpenseCodes.Of(e.Category)))];
    }
}
