using Microsoft.EntityFrameworkCore;
using StoneFlow.Expenses.Application;
using StoneFlow.Shared.Application.Operations;
using StoneFlow.Shared.Kernel.Operations;

namespace StoneFlow.Expenses.Infrastructure.Persistence;

/// <summary>
/// Operacje pieniężne modułu Expenses (SF-17): opłacone wydatki (data zapłaty, kwota brutto ze
/// znakiem) i wypłaty właścicieli. Projekcja w SQL z filtrem zakresu dat i limitem; nieopłacone
/// wydatki nie są czytane. Zapytania idą po kolei (wspólne połączenie modułów).
/// </summary>
internal sealed class ExpenseCashOperationSource(ExpensesDbContext context) : ICashOperationSource
{
    public IReadOnlySet<CashOperationKind> Kinds { get; } = new HashSet<CashOperationKind>
    {
        CashOperationKind.PaidExpense,
        CashOperationKind.OwnerWithdrawal,
    };

    public async Task<IReadOnlyList<CashOperation>> ListAsync(
        DateOnly from, DateOnly to, IReadOnlySet<CashOperationKind> kinds, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        var operations = new List<CashOperation>();

        if (kinds.Contains(CashOperationKind.PaidExpense))
        {
            var expenses = await context.Expenses.AsNoTracking()
                .Where(e => e.PaidOn != null && e.PaidOn >= from && e.PaidOn <= to)
                .OrderBy(e => e.PaidOn)
                .ThenBy(e => e.Id)
                .Take(limit)
                .Select(e => new { e.Id, PaidOn = e.PaidOn!.Value, e.GrossAmount, e.PaymentMethod, e.Description, e.Category })
                .ToListAsync(cancellationToken);

            operations.AddRange(expenses.Select(e => new CashOperation(
                CashOperationKind.PaidExpense,
                e.Id,
                e.PaidOn,
                e.GrossAmount,
                e.PaymentMethod is { } method ? ExpenseCodes.Of(method) : null,
                null,
                e.Description,
                ExpenseCodes.Of(e.Category),
                e.Id,
                e.Description)));
        }

        // Limit wyczerpany przez wydatki: druga tabela nie jest odpytywana (bez TOP 0).
        var remaining = limit - operations.Count;
        if (kinds.Contains(CashOperationKind.OwnerWithdrawal) && remaining > 0)
        {
            var withdrawals = await context.OwnerWithdrawals.AsNoTracking()
                .Where(w => w.WithdrawnOn >= from && w.WithdrawnOn <= to)
                .OrderBy(w => w.WithdrawnOn)
                .ThenBy(w => w.Id)
                .Take(remaining)
                .Select(w => new { w.Id, w.WithdrawnOn, w.Amount, w.Method, w.Person })
                .ToListAsync(cancellationToken);

            operations.AddRange(withdrawals.Select(w => new CashOperation(
                CashOperationKind.OwnerWithdrawal,
                w.Id,
                w.WithdrawnOn,
                w.Amount,
                OwnerWithdrawalCodes.Of(w.Method),
                null,
                w.Person,
                null,
                w.Id,
                w.Person)));
        }

        return operations;
    }
}
