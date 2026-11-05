using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Payments;

namespace StoneFlow.Orders.Infrastructure.Persistence;

/// <summary>
/// Zapis wpisów (wpłat albo zwrotów). Tłumaczy znane błędy bazy na wyniki portu, jak
/// <see cref="OrderStore"/>: konflikt <c>rowversion</c> (<see cref="DbUpdateConcurrencyException"/>)
/// przy poprawie i usunięciu oraz naruszenie unikalności przy dodaniu (2601, 2627).
/// </summary>
internal sealed class OrderEntryStore<TEntry>(OrdersDbContext context) : IOrderEntryStore<TEntry>
    where TEntry : OrderEntry
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    private DbSet<TEntry> Entries => context.Set<TEntry>();

    public async Task<StoredEntry<TEntry>?> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var entry = await Entries.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        return entry is null ? null : new StoredEntry<TEntry>(entry, Version(entry));
    }

    public async Task<StoreSaveResult> AddAsync(TEntry entry, CancellationToken cancellationToken)
    {
        Entries.Add(entry);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation })
        {
            // Wynik z oryginalnym wyjątkiem: wywołujący po wycofaniu rozstrzyga, czy to klucz idempotencji.
            return new StoreSaveResult(StoreSaveStatus.DuplicateRequest, Error: exception);
        }

        return new StoreSaveResult(StoreSaveStatus.Saved, Version(entry));
    }

    public Task<StoreSaveResult> SaveAsync(TEntry entry, byte[] expectedVersion, CancellationToken cancellationToken) =>
        SaveWithVersionAsync(entry, expectedVersion, remove: false, cancellationToken);

    public Task<StoreSaveResult> DeleteAsync(TEntry entry, byte[] expectedVersion, CancellationToken cancellationToken) =>
        SaveWithVersionAsync(entry, expectedVersion, remove: true, cancellationToken);

    public async Task<EntryCreationMatch?> FindByCreationRequestAsync(Guid creationRequestId, CancellationToken cancellationToken)
    {
        var row = await Entries.AsNoTracking()
            .Where(e => e.CreationRequestId == creationRequestId)
            .Select(e => new { e.Id, e.OrderId, e.CreationRequestHash, Version = EF.Property<byte[]>(e, OrderEntryConfiguration.VersionProperty) })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : new EntryCreationMatch(row.Id, row.OrderId, row.CreationRequestHash ?? [], row.Version);
    }

    private async Task<StoreSaveResult> SaveWithVersionAsync(TEntry entry, byte[] expectedVersion, bool remove, CancellationToken cancellationToken)
    {
        // UPDATE/DELETE … WHERE Version = @wersja klienta: wiersz zmieniony w międzyczasie nie zostanie nadpisany ani usunięty.
        context.Entry(entry).Property<byte[]>(OrderEntryConfiguration.VersionProperty).OriginalValue = expectedVersion;
        if (remove)
        {
            Entries.Remove(entry);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new StoreSaveResult(StoreSaveStatus.Conflict);
        }

        return new StoreSaveResult(StoreSaveStatus.Saved, remove ? null : Version(entry));
    }

    private byte[] Version(TEntry entry) =>
        context.Entry(entry).Property<byte[]>(OrderEntryConfiguration.VersionProperty).CurrentValue;
}
