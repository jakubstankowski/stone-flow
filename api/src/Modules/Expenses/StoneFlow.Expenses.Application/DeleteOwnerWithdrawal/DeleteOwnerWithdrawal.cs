using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.Persistence;

namespace StoneFlow.Expenses.Application.DeleteOwnerWithdrawal;

/// <summary>Usunięcie wypłaty (fizyczne, ślad w audycie); <paramref name="Version"/> z odczytu (base64).</summary>
public sealed record DeleteOwnerWithdrawalCommand(Guid AccountId, Guid WithdrawalId, string? Version) : ICommand<OwnerWithdrawalChangeResult>;

/// <summary>
/// Walidacja wersji (400), potem w jednej jednostce pracy: istnienie (404), wersja (409), audyt
/// <c>Deleted</c> ze wszystkimi polami i usunięcie z oczekiwaną wersją. Konflikt w bazie wycofuje
/// także wpis audytu.
/// </summary>
public sealed class DeleteOwnerWithdrawalHandler(IUnitOfWork unitOfWork, IAuditLog auditLog, IOwnerWithdrawalStore store)
    : ICommandHandler<DeleteOwnerWithdrawalCommand, OwnerWithdrawalChangeResult>
{
    public Task<OwnerWithdrawalChangeResult> HandleAsync(DeleteOwnerWithdrawalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new FieldErrors();
        if (OwnerWithdrawalVersion.Validate(command.Version, errors) is not { } version)
        {
            return Task.FromResult(OwnerWithdrawalChangeResult.Invalid(errors));
        }

        return unitOfWork.ExecuteInTransactionAsync(
            ct => DeleteAsync(command, version, ct),
            commitWhen: result => result.Outcome == OwnerWithdrawalChangeOutcome.Saved,
            cancellationToken);
    }

    private async Task<OwnerWithdrawalChangeResult> DeleteAsync(DeleteOwnerWithdrawalCommand command, byte[] version, CancellationToken cancellationToken)
    {
        var stored = await store.LoadAsync(command.WithdrawalId, cancellationToken);
        if (stored is null)
        {
            return new OwnerWithdrawalChangeResult(OwnerWithdrawalChangeOutcome.NotFound);
        }

        if (!stored.Version.AsSpan().SequenceEqual(version))
        {
            return new OwnerWithdrawalChangeResult(OwnerWithdrawalChangeOutcome.Conflict);
        }

        await auditLog.RecordAsync(OwnerWithdrawalAudit.Deleted(stored.Withdrawal, AuditAuthor.Account(command.AccountId)), cancellationToken);

        var deleted = await store.DeleteAsync(stored.Withdrawal, version, cancellationToken);
        return deleted.Status == StoreSaveStatus.Saved
            ? new OwnerWithdrawalChangeResult(OwnerWithdrawalChangeOutcome.Saved)
            : new OwnerWithdrawalChangeResult(OwnerWithdrawalChangeOutcome.Conflict);
    }
}
