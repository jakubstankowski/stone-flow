using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Domain.Expenses;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Application.Persistence;

namespace StoneFlow.Expenses.Application;

public enum ExpenseChangeOutcome
{
    /// <summary>Zapisano zmiany albo nic się nie zmieniło (wersja bez zmian); po usunięciu bez wersji.</summary>
    Saved,
    ValidationFailed,
    NotFound,

    /// <summary>Wersja klienta jest nieaktualna (sprawdzana przed regułą domeny).</summary>
    Conflict,

    /// <summary>„Oznacz jako opłacony” przy aktualnej wersji na wydatku już opłaconym.</summary>
    AlreadyPaid,

    /// <summary>„Cofnij oznaczenie zapłaty” przy aktualnej wersji na wydatku bez daty zapłaty.</summary>
    NotPaid,
}

/// <summary>Wynik zmiany wydatku (poprawa, akcje zapłaty, usunięcie); <see cref="Version"/> po zapisie.</summary>
public sealed record ExpenseChangeResult(ExpenseChangeOutcome Outcome, byte[]? Version = null, FieldErrors? Errors = null)
{
    public static ExpenseChangeResult Invalid(FieldErrors errors) => new(ExpenseChangeOutcome.ValidationFailed, Errors: errors);
}

/// <summary>
/// Wspólny przebieg zmiany wydatku dla poprawy i akcji zapłaty, w jednej jednostce pracy:
/// wczytanie (404), wersja (409 — zawsze przed regułą domeny), operacja domeny (409 przy
/// naruszeniu reguły zapłaty), brak zmian (sukces bez zapisu i audytu), zapis z oczekiwaną wersją
/// (409 przy konflikcie w bazie) i audyt <c>Updated</c> (z rodzicem — definicją — dla wystąpienia cyklicznego). Zatwierdzany jest tylko wynik
/// <see cref="ExpenseChangeOutcome.Saved"/>. Walidację wejścia robi handler przed wywołaniem.
/// </summary>
public sealed class ExpenseChangeRunner(IUnitOfWork unitOfWork, IAuditLog auditLog, IExpenseStore store, TimeProvider timeProvider)
{
    public Task<ExpenseChangeResult> RunAsync(
        Guid accountId,
        Guid expenseId,
        byte[] version,
        Func<Expense, DateTime, ExpenseChangeSet> change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(change);

        return unitOfWork.ExecuteInTransactionAsync(
            ct => ChangeAsync(accountId, expenseId, version, change, ct),
            commitWhen: result => result.Outcome == ExpenseChangeOutcome.Saved,
            cancellationToken);
    }

    private async Task<ExpenseChangeResult> ChangeAsync(
        Guid accountId,
        Guid expenseId,
        byte[] version,
        Func<Expense, DateTime, ExpenseChangeSet> change,
        CancellationToken cancellationToken)
    {
        var stored = await store.LoadAsync(expenseId, cancellationToken);
        if (stored is null)
        {
            return new ExpenseChangeResult(ExpenseChangeOutcome.NotFound);
        }

        if (!stored.Version.AsSpan().SequenceEqual(version))
        {
            return new ExpenseChangeResult(ExpenseChangeOutcome.Conflict);
        }

        var result = change(stored.Expense, timeProvider.GetUtcNow().UtcDateTime);
        switch (result.Violation)
        {
            case ExpenseRuleViolation.AlreadyPaid:
                return new ExpenseChangeResult(ExpenseChangeOutcome.AlreadyPaid);
            case ExpenseRuleViolation.NotPaid:
                return new ExpenseChangeResult(ExpenseChangeOutcome.NotPaid);
        }

        if (result.Changes.Count == 0)
        {
            return new ExpenseChangeResult(ExpenseChangeOutcome.Saved, stored.Version);
        }

        var saved = await store.SaveAsync(stored.Expense, version, cancellationToken);
        if (saved.Status == StoreSaveStatus.Conflict)
        {
            return new ExpenseChangeResult(ExpenseChangeOutcome.Conflict);
        }

        await auditLog.RecordAsync(ExpenseAudit.Updated(stored.Expense, result.Changes, AuditAuthor.Account(accountId), ExpenseAudit.ParentOf(stored.Expense)), cancellationToken);
        return new ExpenseChangeResult(ExpenseChangeOutcome.Saved, saved.Version);
    }
}
