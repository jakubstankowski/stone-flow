using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Domain.Expenses;

namespace StoneFlow.Expenses.Infrastructure.Persistence;

/// <summary>
/// Zapis wydatków. Tłumaczy znane błędy bazy na wyniki portu: konflikt <c>rowversion</c>
/// (<see cref="DbUpdateConcurrencyException"/>, także przy usunięciu) i naruszenie unikalności przy
/// dodaniu (2601, 2627).
/// </summary>
internal sealed class ExpenseStore(ExpensesDbContext context) : IExpenseStore
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public async Task<StoredExpense?> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var expense = await context.Expenses.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        return expense is null ? null : new StoredExpense(expense, Version(expense));
    }

    public async Task<StoreSaveResult> AddAsync(Expense expense, CancellationToken cancellationToken)
    {
        context.Expenses.Add(expense);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation })
        {
            // Wynik z oryginalnym wyjątkiem: wywołujący po wycofaniu rozstrzyga, czy to klucz idempotencji.
            return new StoreSaveResult(StoreSaveStatus.DuplicateRequest, Error: exception);
        }

        return new StoreSaveResult(StoreSaveStatus.Saved, Version(expense));
    }

    public async Task<StoreSaveResult> SaveAsync(Expense expense, byte[] expectedVersion, CancellationToken cancellationToken)
    {
        // UPDATE … WHERE Version = @wersja klienta: wiersz zmieniony w międzyczasie nie zostanie nadpisany.
        context.Entry(expense).Property<byte[]>(ExpenseConfiguration.VersionProperty).OriginalValue = expectedVersion;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new StoreSaveResult(StoreSaveStatus.Conflict);
        }

        return new StoreSaveResult(StoreSaveStatus.Saved, Version(expense));
    }

    public async Task<StoreSaveResult> DeleteAsync(Expense expense, byte[] expectedVersion, CancellationToken cancellationToken)
    {
        // DELETE … WHERE Version = @wersja klienta.
        context.Entry(expense).Property<byte[]>(ExpenseConfiguration.VersionProperty).OriginalValue = expectedVersion;
        context.Expenses.Remove(expense);
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

    private byte[] Version(Expense expense) =>
        context.Entry(expense).Property<byte[]>(ExpenseConfiguration.VersionProperty).CurrentValue;
}
