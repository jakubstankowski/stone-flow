using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Domain.Recurring;

namespace StoneFlow.Expenses.Infrastructure.Persistence;

/// <summary>
/// Zapis definicji, wystąpień z rejestrem i kluczy idempotencji. Tłumaczy znane błędy bazy na wyniki
/// portu: konflikt <c>rowversion</c> (<see cref="DbUpdateConcurrencyException"/>), naruszenie klucza
/// (2601, 2627) — przy wystąpieniach to <c>PK_RecurringExpensePeriods</c> (nowe wydatki mają nowe id),
/// przy kluczu żądania PK <c>RecurringDefinitionRequests</c> — i klucza obcego (547: definicja usunięta
/// w trakcie). Zakleszczenie (1205) nie jest tłumaczone: wyjątek idzie do jednostki pracy.
/// </summary>
internal sealed class RecurringExpenseStore(ExpensesDbContext context) : IRecurringExpenseStore
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;
    private const int ForeignKeyViolation = 547;

    /// <summary>
    /// Jedyne naruszenie 547 tłumaczone na „brak definicji”: klucz obcy rejestru do definicji. Ten sam
    /// numer zgłasza SQL Server dla <c>CHECK</c> — takie naruszenie to błąd programu i idzie dalej.
    /// </summary>
    internal const string PeriodDefinitionForeignKey = "FK_RecurringExpensePeriods_RecurringExpenseDefinitions_DefinitionId";

    public async Task<StoredRecurringDefinition?> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        // Dni i wyłączenia to części definicji (owned) — wczytują się razem z nią.
        var definition = await context.RecurringDefinitions.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        return definition is null ? null : new StoredRecurringDefinition(definition, Version(definition));
    }

    public async Task<IReadOnlyDictionary<RecurringPeriod, DateOnly>> RegisteredPeriodsAsync(Guid definitionId, CancellationToken cancellationToken)
    {
        var rows = await context.RecurringPeriods.AsNoTracking()
            .Where(period => period.DefinitionId == definitionId)
            .Select(period => new { period.Period, period.DueDate })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => RecurringPeriod.Of(row.Period), row => row.DueDate);
    }

    public async Task<StoreSaveResult> AddAsync(RecurringExpenseDefinition definition, CancellationToken cancellationToken)
    {
        context.RecurringDefinitions.Add(definition);
        await context.SaveChangesAsync(cancellationToken);
        return new StoreSaveResult(StoreSaveStatus.Saved, Version(definition));
    }

    public async Task<StoreSaveResult> SaveAsync(RecurringExpenseDefinition definition, byte[] expectedVersion, CancellationToken cancellationToken)
    {
        // UPDATE … WHERE Version = @wersja klienta.
        context.Entry(definition).Property<byte[]>(RecurringExpenseDefinitionConfiguration.VersionProperty).OriginalValue = expectedVersion;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new StoreSaveResult(StoreSaveStatus.Conflict);
        }

        return new StoreSaveResult(StoreSaveStatus.Saved, Version(definition));
    }

    public async Task<StoreSaveResult> DeleteAsync(RecurringExpenseDefinition definition, byte[] expectedVersion, CancellationToken cancellationToken)
    {
        // DELETE … WHERE Version = @wersja klienta; dni, wyłączenia i rejestr usuwa kaskada.
        context.Entry(definition).Property<byte[]>(RecurringExpenseDefinitionConfiguration.VersionProperty).OriginalValue = expectedVersion;
        context.RecurringDefinitions.Remove(definition);
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

    public async Task<OccurrenceSaveStatus> AddOccurrencesAsync(IReadOnlyList<RecurringOccurrence> occurrences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        foreach (var occurrence in occurrences)
        {
            context.Expenses.Add(occurrence.Expense);
            context.RecurringPeriods.Add(occurrence.Period);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation })
        {
            return OccurrenceSaveStatus.DuplicatePeriod;
        }
        catch (DbUpdateException exception) when (IsDefinitionForeignKeyViolation(exception))
        {
            return OccurrenceSaveStatus.DefinitionMissing;
        }

        return OccurrenceSaveStatus.Saved;
    }

    public async Task<StoreSaveResult> AddRequestAsync(RecurringDefinitionRequest request, CancellationToken cancellationToken)
    {
        context.RecurringDefinitionRequests.Add(request);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation })
        {
            // Wynik z oryginalnym wyjątkiem: wywołujący po wycofaniu czyta klucz na nowo.
            return new StoreSaveResult(StoreSaveStatus.DuplicateRequest, Error: exception);
        }

        return new StoreSaveResult(StoreSaveStatus.Saved);
    }

    private static bool IsDefinitionForeignKeyViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: ForeignKeyViolation } sql
        && sql.Message.Contains(PeriodDefinitionForeignKey, StringComparison.Ordinal);

    private byte[] Version(RecurringExpenseDefinition definition) =>
        context.Entry(definition).Property<byte[]>(RecurringExpenseDefinitionConfiguration.VersionProperty).CurrentValue;
}
