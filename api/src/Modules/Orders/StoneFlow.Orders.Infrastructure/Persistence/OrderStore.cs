using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Orders;

namespace StoneFlow.Orders.Infrastructure.Persistence;

/// <summary>
/// Zapis zamówień. Tłumaczy znane błędy bazy na wyniki portu: konflikt <c>rowversion</c>
/// (<see cref="DbUpdateConcurrencyException"/>) i naruszenie unikalności przy dodaniu (2601, 2627).
/// </summary>
internal sealed class OrderStore(OrdersDbContext context) : IOrderStore
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public async Task<StoredOrder?> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await context.Orders.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        return order is null ? null : new StoredOrder(order, Version(order));
    }

    public async Task<StoreSaveResult> AddAsync(Order order, CancellationToken cancellationToken)
    {
        context.Orders.Add(order);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation })
        {
            // Wynik z oryginalnym wyjątkiem: wywołujący po wycofaniu rozstrzyga, czy to klucz idempotencji.
            return new StoreSaveResult(StoreSaveStatus.DuplicateRequest, Error: exception);
        }

        return new StoreSaveResult(StoreSaveStatus.Saved, Version(order));
    }

    public async Task<StoreSaveResult> SaveAsync(Order order, byte[] expectedVersion, CancellationToken cancellationToken)
    {
        // UPDATE … WHERE Version = @wersja klienta: wiersz zmieniony w międzyczasie nie zostanie nadpisany.
        context.Entry(order).Property<byte[]>(OrderConfiguration.VersionProperty).OriginalValue = expectedVersion;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new StoreSaveResult(StoreSaveStatus.Conflict);
        }

        return new StoreSaveResult(StoreSaveStatus.Saved, Version(order));
    }

    private byte[] Version(Order order) =>
        context.Entry(order).Property<byte[]>(OrderConfiguration.VersionProperty).CurrentValue;
}
