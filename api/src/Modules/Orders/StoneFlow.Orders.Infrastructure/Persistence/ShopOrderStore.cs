using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Orders;

namespace StoneFlow.Orders.Infrastructure.Persistence;

/// <summary>Znacznik usunięcia zamówienia ze sklepu, którego ERP jeszcze nie ma.</summary>
internal sealed class ShopDeletion
{
    public required string ExternalId { get; init; }

    public DateTime ReceivedAtUtc { get; init; }
}

internal sealed class ShopOrderStore(OrdersDbContext context) : IShopOrderStore
{
    private const int LockTimeoutMs = 15_000;

    public async Task LockAsync(string externalId, CancellationToken cancellationToken)
    {
        var resource = "orders:shop:" + externalId;
        var result = await context.Database
            .SqlQuery<int>($"""
                DECLARE @result int;
                EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {LockTimeoutMs};
                SELECT @result AS [Value];
                """)
            .ToListAsync(cancellationToken);
        if (result is not [>= 0])
        {
            throw new InvalidOperationException($"Nie uzyskano blokady zamówienia ze sklepu (wynik {string.Join(",", result)}).");
        }
    }

    public async Task<StoredOrder?> FindAsync(string externalId, CancellationToken cancellationToken)
    {
        var order = await context.Orders.SingleOrDefaultAsync(
            o => o.Source == OrderSource.WooCommerce && o.ExternalId == externalId, cancellationToken);
        return order is null ? null : new StoredOrder(order, (byte[])context.Entry(order).Property(OrderConfiguration.VersionProperty).CurrentValue!);
    }

    public async Task<DateTime?> FindDeletionAsync(string externalId, CancellationToken cancellationToken) =>
        await context.ShopDeletions.Where(d => d.ExternalId == externalId).Select(d => (DateTime?)d.ReceivedAtUtc).SingleOrDefaultAsync(cancellationToken);

    public async Task<StoreSaveStatus> RecordDeletionAsync(string externalId, DateTime receivedAtUtc, CancellationToken cancellationToken)
    {
        if (await context.ShopDeletions.AnyAsync(d => d.ExternalId == externalId, cancellationToken))
        {
            return StoreSaveStatus.Saved;
        }

        context.ShopDeletions.Add(new ShopDeletion { ExternalId = externalId, ReceivedAtUtc = receivedAtUtc });
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return StoreSaveStatus.Saved;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return StoreSaveStatus.DuplicateRequest;
        }
    }

    public Task RemoveDeletionAsync(string externalId, CancellationToken cancellationToken) =>
        context.ShopDeletions.Where(d => d.ExternalId == externalId).ExecuteDeleteAsync(cancellationToken);
}
