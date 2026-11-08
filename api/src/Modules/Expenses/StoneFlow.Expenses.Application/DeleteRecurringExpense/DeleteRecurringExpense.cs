using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Application.Recurring;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Expenses.Application.DeleteRecurringExpense;

/// <summary>Usunięcie definicji (fizyczne, ślad w audycie); <paramref name="Version"/> z odczytu (base64).</summary>
public sealed record DeleteRecurringExpenseCommand(Guid AccountId, Guid DefinitionId, string? Version) : ICommand<RecurringChangeResult>;

/// <summary>
/// Wersja (400), potem w jednej jednostce pracy: blokada, istnienie (404), wersja (409), audyt
/// <c>Deleted</c> ze wszystkimi niepustymi polami i usunięcie z wersją. Kaskada usuwa dni, wyłączenia
/// i rejestr; klucze idempotencji i wystąpienia zostają.
/// </summary>
public sealed class DeleteRecurringExpenseHandler(RecurringChangeRunner runner, IRecurringExpenseStore store, IAuditLog auditLog)
    : ICommandHandler<DeleteRecurringExpenseCommand, RecurringChangeResult>
{
    public Task<RecurringChangeResult> HandleAsync(DeleteRecurringExpenseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new FieldErrors();
        if (ExpenseVersion.Validate(command.Version, errors) is not { } version)
        {
            return Task.FromResult(RecurringChangeResult.Invalid(errors));
        }

        return runner.RunAsync(
            command.DefinitionId,
            version,
            async (context, ct) =>
            {
                await auditLog.RecordAsync(RecurringExpenseAudit.Deleted(context.Definition, AuditAuthor.Account(command.AccountId)), ct);
                var deleted = await store.DeleteAsync(context.Definition, version, ct);
                return new RecurringChangeResult(deleted.Status == StoreSaveStatus.Saved ? RecurringChangeOutcome.Saved : RecurringChangeOutcome.Conflict);
            },
            cancellationToken);
    }
}
