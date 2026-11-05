using Microsoft.EntityFrameworkCore;
using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Orders;

namespace StoneFlow.Orders.Infrastructure.Persistence;

/// <summary>
/// Blokada wiersza zamówienia w transakcji jednostki pracy (interceptor SF-7 dołącza polecenia do
/// transakcji): <c>SELECT … WITH (UPDLOCK, ROWLOCK)</c> trzyma blokadę aktualizacji do końca
/// transakcji, więc zapisy wpisów jednego zamówienia wykonują się po kolei. Sumy wpisów są czytane
/// osobnym poleceniem, dopiero po uzyskaniu blokady. Odczyt nie zmienia <c>rowversion</c> zamówienia.
/// </summary>
internal sealed class OrderLock(OrdersDbContext context) : IOrderLock
{
    public async Task<LockedOrder?> LockAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var cancelled = nameof(FulfillmentStatus.Cancelled);
        var rows = await context.Database
            .SqlQuery<LockRow>(
                $"SELECT [Number], [Value], CAST(CASE WHEN [FulfillmentStatus] = {cancelled} THEN 1 ELSE 0 END AS bit) AS [IsCancelled] FROM [orders].[Orders] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {orderId}")
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return null;
        }

        var payments = await context.Payments.Where(payment => payment.OrderId == orderId).SumAsync(payment => payment.Amount, cancellationToken);
        var refunds = await context.Refunds.Where(refund => refund.OrderId == orderId).SumAsync(refund => refund.Amount, cancellationToken);
        var row = rows[0];
        return new LockedOrder(row.Number, row.Value, row.IsCancelled, payments, refunds);
    }

    private sealed record LockRow(string Number, decimal Value, bool IsCancelled);
}
