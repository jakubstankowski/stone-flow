using Microsoft.EntityFrameworkCore;
using StoneFlow.Orders.Application;
using StoneFlow.Shared.Application.Operations;
using StoneFlow.Shared.Kernel.Operations;

namespace StoneFlow.Orders.Infrastructure.Persistence;

/// <summary>
/// Operacje pieniężne modułu Orders (SF-17): wpłaty i zwroty po dacie wpisu (nie dacie zamówienia),
/// z numerem zamówienia jako opisem. Bez filtra stanu, źródła ani anulowania zamówienia: każda
/// wpłata i każdy zwrot to rzeczywiste pieniądze. Korekty sprzedaży nie są operacją pieniężną i
/// nie są czytane. Projekcja w SQL z limitem; zapytania idą po kolei (wspólne połączenie modułów).
/// </summary>
internal sealed class OrderCashOperationSource(OrdersDbContext context) : ICashOperationSource
{
    public IReadOnlySet<CashOperationKind> Kinds { get; } = new HashSet<CashOperationKind>
    {
        CashOperationKind.Payment,
        CashOperationKind.Refund,
    };

    public async Task<IReadOnlyList<CashOperation>> ListAsync(
        DateOnly from, DateOnly to, IReadOnlySet<CashOperationKind> kinds, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        var operations = new List<CashOperation>();

        if (kinds.Contains(CashOperationKind.Payment))
        {
            var payments = await context.Payments.AsNoTracking()
                .Where(p => p.Date >= from && p.Date <= to)
                .Join(context.Orders, p => p.OrderId, o => o.Id, (p, o) => new { p.Id, p.Date, p.Amount, p.Method, p.OrderId, o.Number })
                .OrderBy(p => p.Date)
                .ThenBy(p => p.Id)
                .Take(limit)
                .ToListAsync(cancellationToken);

            operations.AddRange(payments.Select(p => new CashOperation(
                CashOperationKind.Payment,
                p.Id,
                p.Date,
                p.Amount,
                OrderCodes.Of(p.Method),
                null,
                p.Number,
                null,
                p.OrderId,
                p.Number)));
        }

        // Limit wyczerpany przez wpłaty: druga tabela nie jest odpytywana (bez TOP 0).
        var remaining = limit - operations.Count;
        if (kinds.Contains(CashOperationKind.Refund) && remaining > 0)
        {
            var refunds = await context.Refunds.AsNoTracking()
                .Where(r => r.Date >= from && r.Date <= to)
                .Join(context.Orders, r => r.OrderId, o => o.Id, (r, o) => new { r.Id, r.Date, r.Amount, r.Method, r.Kind, r.OrderId, o.Number })
                .OrderBy(r => r.Date)
                .ThenBy(r => r.Id)
                .Take(remaining)
                .ToListAsync(cancellationToken);

            operations.AddRange(refunds.Select(r => new CashOperation(
                CashOperationKind.Refund,
                r.Id,
                r.Date,
                r.Amount,
                OrderCodes.Of(r.Method),
                OrderCodes.Of(r.Kind),
                r.Number,
                null,
                r.OrderId,
                r.Number)));
        }

        return operations;
    }
}
