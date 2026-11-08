using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.Persistence;

namespace StoneFlow.Expenses.Application.DeleteExpense;

/// <summary>Usunięcie wydatku (fizyczne, ślad w audycie); <paramref name="Version"/> z odczytu (base64).</summary>
public sealed record DeleteExpenseCommand(Guid AccountId, Guid ExpenseId, string? Version) : ICommand<ExpenseChangeResult>;

/// <summary>
/// Walidacja wersji (400), potem w jednej jednostce pracy: istnienie (404), wersja (409), audyt
/// <c>Deleted</c> ze wszystkimi niepustymi polami i usunięcie z oczekiwaną wersją. Konflikt w bazie
/// wycofuje także wpis audytu.
/// </summary>
public sealed class DeleteExpenseHandler(IUnitOfWork unitOfWork, IAuditLog auditLog, IExpenseStore store)
    : ICommandHandler<DeleteExpenseCommand, ExpenseChangeResult>
{
    public Task<ExpenseChangeResult> HandleAsync(DeleteExpenseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new FieldErrors();
        if (ExpenseVersion.Validate(command.Version, errors) is not { } version)
        {
            return Task.FromResult(ExpenseChangeResult.Invalid(errors));
        }

        return unitOfWork.ExecuteInTransactionAsync(
            ct => DeleteAsync(command, version, ct),
            commitWhen: result => result.Outcome == ExpenseChangeOutcome.Saved,
            cancellationToken);
    }

    private async Task<ExpenseChangeResult> DeleteAsync(DeleteExpenseCommand command, byte[] version, CancellationToken cancellationToken)
    {
        var stored = await store.LoadAsync(command.ExpenseId, cancellationToken);
        if (stored is null)
        {
            return new ExpenseChangeResult(ExpenseChangeOutcome.NotFound);
        }

        if (!stored.Version.AsSpan().SequenceEqual(version))
        {
            return new ExpenseChangeResult(ExpenseChangeOutcome.Conflict);
        }

        await auditLog.RecordAsync(ExpenseAudit.Deleted(stored.Expense, AuditAuthor.Account(command.AccountId), ExpenseAudit.ParentOf(stored.Expense)), cancellationToken);

        var deleted = await store.DeleteAsync(stored.Expense, version, cancellationToken);
        return deleted.Status == StoreSaveStatus.Saved
            ? new ExpenseChangeResult(ExpenseChangeOutcome.Saved)
            : new ExpenseChangeResult(ExpenseChangeOutcome.Conflict);
    }
}
