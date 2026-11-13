using Microsoft.EntityFrameworkCore;
using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Payments;

namespace StoneFlow.Orders.Infrastructure.Persistence;

/// <summary>
/// Dane wpisów zamówienia dla anonimizacji (SF-22): fakty retencji (najpóźniejsza data i czas zmiany
/// wpłat, zwrotów i korekt, po kolei — wspólne połączenie) oraz śledzone zwroty i korekty do zmiany powodu.
/// </summary>
internal sealed class OrderAnonymizationStore(OrdersDbContext context) : IOrderAnonymizationStore
{
    public async Task<EntryRetentionFacts> GetRetentionFactsAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var payments = await FactsAsync(context.Payments, orderId, cancellationToken);
        var refunds = await FactsAsync(context.Refunds, orderId, cancellationToken);
        var corrections = await FactsAsync(context.SalesCorrections, orderId, cancellationToken);

        DateOnly? lastDate = null;
        DateTime? lastChange = null;
        foreach (var facts in new[] { payments, refunds, corrections })
        {
            if (facts is null)
            {
                continue;
            }

            if (lastDate is null || facts.Date > lastDate)
            {
                lastDate = facts.Date;
            }

            if (lastChange is null || facts.Changed > lastChange)
            {
                lastChange = facts.Changed;
            }
        }

        return new EntryRetentionFacts(lastDate, lastChange is { } utc ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : null);
    }

    public async Task<ReasonEntries> LoadReasonEntriesAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var refunds = await context.Refunds.Where(refund => refund.OrderId == orderId).ToListAsync(cancellationToken);
        var corrections = await context.SalesCorrections.Where(correction => correction.OrderId == orderId).ToListAsync(cancellationToken);
        return new ReasonEntries(refunds, corrections);
    }

    private static Task<EntryFacts?> FactsAsync<TEntry>(IQueryable<TEntry> entries, Guid orderId, CancellationToken cancellationToken)
        where TEntry : OrderEntry =>
        entries
            .Where(entry => entry.OrderId == orderId)
            .GroupBy(_ => 1)
            .Select(group => new EntryFacts(group.Max(entry => entry.Date), group.Max(entry => entry.UpdatedAtUtc)))
            .SingleOrDefaultAsync(cancellationToken);

    private sealed record EntryFacts(DateOnly Date, DateTime Changed);
}
