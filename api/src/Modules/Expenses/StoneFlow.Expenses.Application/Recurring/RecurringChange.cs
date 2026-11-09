using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Domain.Recurring;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Application.Persistence;

namespace StoneFlow.Expenses.Application.Recurring;

public enum RecurringChangeOutcome
{
    /// <summary>Zapisano (także bez zmian — wersja bez zmian; po usunięciu bez wersji).</summary>
    Saved,
    ValidationFailed,
    NotFound,

    /// <summary>Wersja klienta jest nieaktualna (sprawdzana przed regułą domeny).</summary>
    Conflict,
    AlreadyPaused,
    NotPaused,
    Ended,

    /// <summary>Zmiana dnia miesiąca na definicji wstrzymanej.</summary>
    PausedDayChange,

    /// <summary>Kolizja rejestru okresów przy generowaniu: całość wycofana (503).</summary>
    Busy,
}

/// <summary>Wynik zmiany definicji; <see cref="Version"/> to wersja po zapisie definicji (generowanie jej nie zmienia).</summary>
public sealed record RecurringChangeResult(RecurringChangeOutcome Outcome, byte[]? Version = null, int CreatedOccurrences = 0, FieldErrors? Errors = null)
{
    public static RecurringChangeResult Invalid(FieldErrors errors) => new(RecurringChangeOutcome.ValidationFailed, Errors: errors);

    public static RecurringChangeResult Rejected(RecurringRuleViolation violation) => violation switch
    {
        RecurringRuleViolation.AlreadyPaused => new(RecurringChangeOutcome.AlreadyPaused),
        RecurringRuleViolation.NotPaused => new(RecurringChangeOutcome.NotPaused),
        RecurringRuleViolation.Ended => new(RecurringChangeOutcome.Ended),
        RecurringRuleViolation.PausedDayChange => new(RecurringChangeOutcome.PausedDayChange),
        _ => Invalid(new FieldErrors().Add(RecurringFieldKeys.EndsOn, RecurringMessages.NoDueInRange)),
    };
}

/// <summary>Definicja zablokowana i wczytana w jednostce pracy oraz „dzisiaj” i chwila przypadku użycia.</summary>
public sealed record RecurringChangeContext(StoredRecurringDefinition Stored, DateOnly Today, DateTime NowUtc)
{
    public RecurringExpenseDefinition Definition => Stored.Definition;
}

/// <summary>
/// Wspólny przebieg zmian definicji (edycja, wstrzymanie, wznowienie, usunięcie) w jednej jednostce
/// pracy: blokada wiersza (404 przy braku), wczytanie z dniami i wyłączeniami, wersja (409 — przed
/// regułą domeny), potem krok przypadku użycia. Zatwierdzany jest tylko wynik
/// <see cref="RecurringChangeOutcome.Saved"/>. „Dzisiaj” (Europe/Warsaw) liczy się raz na przypadek
/// użycia. Kroki składają się z <see cref="SaveAsync"/> i <see cref="GenerateAsync"/>.
/// </summary>
public sealed class RecurringChangeRunner(
    IUnitOfWork unitOfWork,
    IRecurringDefinitionLock definitionLock,
    IRecurringExpenseStore store,
    IAuditLog auditLog,
    OccurrenceGenerator generator,
    TimeProvider timeProvider)
{
    public Task<RecurringChangeResult> RunAsync(
        Guid definitionId,
        byte[] version,
        Func<RecurringChangeContext, CancellationToken, Task<RecurringChangeResult>> step,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(step);

        return unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                if (!await definitionLock.LockAsync(definitionId, ct))
                {
                    return new RecurringChangeResult(RecurringChangeOutcome.NotFound);
                }

                var stored = await store.LoadAsync(definitionId, ct);
                if (stored is null)
                {
                    return new RecurringChangeResult(RecurringChangeOutcome.NotFound);
                }

                if (!stored.Version.AsSpan().SequenceEqual(version))
                {
                    return new RecurringChangeResult(RecurringChangeOutcome.Conflict);
                }

                var now = timeProvider.GetUtcNow();
                return await step(new RecurringChangeContext(stored, WarsawTime.Today(now), now.UtcDateTime), ct);
            },
            commitWhen: result => result.Outcome == RecurringChangeOutcome.Saved,
            cancellationToken);
    }

    /// <summary>Zapis definicji z oczekiwaną wersją (409 przy konflikcie w bazie) i audyt <c>Updated</c> zmienionych pól.</summary>
    public async Task<RecurringChangeResult> SaveAsync(
        RecurringChangeContext context, IReadOnlyList<RecurringFieldChange> changes, Guid accountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(changes);

        var saved = await store.SaveAsync(context.Definition, context.Stored.Version, cancellationToken);
        if (saved.Status != StoreSaveStatus.Saved)
        {
            return new RecurringChangeResult(RecurringChangeOutcome.Conflict);
        }

        await auditLog.RecordAsync(RecurringExpenseAudit.Updated(context.Definition, changes, AuditAuthor.Account(accountId)), cancellationToken);
        return new RecurringChangeResult(RecurringChangeOutcome.Saved, saved.Version);
    }

    /// <summary>
    /// Generowanie na dzisiaj z bieżącym stanem definicji; dolicza utworzone wystąpienia do
    /// <paramref name="result"/>. Kolizja rejestru daje <see cref="RecurringChangeOutcome.Busy"/> (bez ponowienia).
    /// </summary>
    public async Task<RecurringChangeResult> GenerateAsync(RecurringChangeContext context, RecurringChangeResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(result);

        var generation = await generator.GenerateAsync(context.Definition, context.Today, cancellationToken);
        return generation.Status switch
        {
            OccurrenceGenerationStatus.Created => result with { CreatedOccurrences = result.CreatedOccurrences + generation.Created },
            OccurrenceGenerationStatus.DuplicatePeriod => new RecurringChangeResult(RecurringChangeOutcome.Busy),

            // Pod blokadą definicja nie może zniknąć; gdyby jednak, wynik to „nie znaleziono”.
            _ => new RecurringChangeResult(RecurringChangeOutcome.NotFound),
        };
    }
}
