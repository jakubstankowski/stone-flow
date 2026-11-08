using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Domain.Recurring;
using StoneFlow.Shared.Application.Audit;

namespace StoneFlow.Expenses.Application.Recurring;

public enum OccurrenceGenerationStatus
{
    Created,

    /// <summary>Kolizja klucza rejestru: wywołujący wycofuje jednostkę pracy (zadanie ponawia, użytkownik dostaje 503).</summary>
    DuplicatePeriod,

    /// <summary>Definicja usunięta w trakcie (klucz obcy).</summary>
    DefinitionMissing,
}

/// <summary>Wynik generowania: liczba utworzonych wystąpień (przy niepowodzeniu jednostka i tak się wycofuje).</summary>
public sealed record OccurrenceGeneration(OccurrenceGenerationStatus Status, int Created);

/// <summary>
/// Operacja modułu „tworzenie wystąpień” (spec SF-16, bez dispatchera), wołana w jednostce pracy
/// wywołującego — handlera zapisu albo komendy zadania — który trzyma już blokadę wiersza definicji
/// (przy dodaniu definicja jest nowa). Okresy należne liczy domena z rejestru wczytanego tutaj;
/// każde wystąpienie powstaje razem z wierszem rejestru i audytem <c>Created</c> (autor „Zadanie
/// automatyczne”, rodzic = definicja). Zapis wsadami po <see cref="BatchSize"/> okresów w tej samej
/// transakcji. Definicji ani jej wersji nie zmienia.
/// </summary>
public sealed class OccurrenceGenerator(IRecurringExpenseStore store, IAuditLog auditLog, TimeProvider timeProvider)
{
    public const int BatchSize = 100;

    public async Task<OccurrenceGeneration> GenerateAsync(RecurringExpenseDefinition definition, DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var registry = await store.RegisteredPeriodsAsync(definition.Id, cancellationToken);
        var periods = definition.DuePeriods(registry, today);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var created = 0;
        foreach (var batch in periods.Chunk(BatchSize))
        {
            var occurrences = batch.Select(period => definition.CreateOccurrence(Guid.NewGuid(), period, nowUtc)).ToList();
            var saved = await store.AddOccurrencesAsync(occurrences, cancellationToken);
            switch (saved)
            {
                case OccurrenceSaveStatus.DuplicatePeriod:
                    return new OccurrenceGeneration(OccurrenceGenerationStatus.DuplicatePeriod, 0);
                case OccurrenceSaveStatus.DefinitionMissing:
                    return new OccurrenceGeneration(OccurrenceGenerationStatus.DefinitionMissing, 0);
            }

            foreach (var occurrence in occurrences)
            {
                var expense = occurrence.Expense;
                await auditLog.RecordAsync(
                    ExpenseAudit.Created(expense, AuditAuthor.AutomatedJob, ExpenseAudit.ParentOf(expense)), cancellationToken);
            }

            created += occurrences.Count;
        }

        return new OccurrenceGeneration(OccurrenceGenerationStatus.Created, created);
    }
}
