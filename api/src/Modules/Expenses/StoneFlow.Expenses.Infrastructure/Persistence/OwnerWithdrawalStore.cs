using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Domain.OwnerWithdrawals;

namespace StoneFlow.Expenses.Infrastructure.Persistence;

/// <summary>
/// Zapis wypłat (przebieg jak <see cref="ExpenseStore"/>). Tłumaczy znane błędy bazy na wyniki
/// portu: konflikt <c>rowversion</c> (<see cref="DbUpdateConcurrencyException"/>, także przy
/// usunięciu) i naruszenie unikalności przy dodaniu (2601, 2627).
/// </summary>
internal sealed class OwnerWithdrawalStore(ExpensesDbContext context) : IOwnerWithdrawalStore
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public async Task<StoredOwnerWithdrawal?> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var withdrawal = await context.OwnerWithdrawals.SingleOrDefaultAsync(w => w.Id == id, cancellationToken);
        return withdrawal is null ? null : new StoredOwnerWithdrawal(withdrawal, Version(withdrawal));
    }

    public async Task<StoreSaveResult> AddAsync(OwnerWithdrawal withdrawal, CancellationToken cancellationToken)
    {
        context.OwnerWithdrawals.Add(withdrawal);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation })
        {
            // Wynik z oryginalnym wyjątkiem: wywołujący po wycofaniu rozstrzyga, czy to klucz idempotencji.
            return new StoreSaveResult(StoreSaveStatus.DuplicateRequest, Error: exception);
        }

        return new StoreSaveResult(StoreSaveStatus.Saved, Version(withdrawal));
    }

    public async Task<StoreSaveResult> SaveAsync(OwnerWithdrawal withdrawal, byte[] expectedVersion, CancellationToken cancellationToken)
    {
        // UPDATE … WHERE Version = @wersja klienta: wiersz zmieniony w międzyczasie nie zostanie nadpisany.
        context.Entry(withdrawal).Property<byte[]>(OwnerWithdrawalConfiguration.VersionProperty).OriginalValue = expectedVersion;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new StoreSaveResult(StoreSaveStatus.Conflict);
        }

        return new StoreSaveResult(StoreSaveStatus.Saved, Version(withdrawal));
    }

    public async Task<StoreSaveResult> DeleteAsync(OwnerWithdrawal withdrawal, byte[] expectedVersion, CancellationToken cancellationToken)
    {
        // DELETE … WHERE Version = @wersja klienta.
        context.Entry(withdrawal).Property<byte[]>(OwnerWithdrawalConfiguration.VersionProperty).OriginalValue = expectedVersion;
        context.OwnerWithdrawals.Remove(withdrawal);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new StoreSaveResult(StoreSaveStatus.Conflict);
        }

        return new StoreSaveResult(StoreSaveStatus.Saved);
    }

    private byte[] Version(OwnerWithdrawal withdrawal) =>
        context.Entry(withdrawal).Property<byte[]>(OwnerWithdrawalConfiguration.VersionProperty).CurrentValue;
}
