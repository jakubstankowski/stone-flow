using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Domain.OwnerWithdrawals;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.Persistence;

namespace StoneFlow.Expenses.Application.EditOwnerWithdrawal;

/// <summary>Poprawa wypłaty; <paramref name="Version"/> odczytana przy wczytaniu (base64).</summary>
public sealed record EditOwnerWithdrawalCommand(Guid AccountId, Guid WithdrawalId, OwnerWithdrawalInput Input, string? Version)
    : ICommand<OwnerWithdrawalChangeResult>;

/// <summary>
/// Walidacja wersji i treści (400), potem w jednej jednostce pracy: istnienie (404), wersja (409),
/// poprawa w domenie, brak zmian — sukces z zapisaną wersją bez zapisu i bez audytu, zapis z
/// oczekiwaną wersją (409 przy konflikcie w bazie) i audyt <c>Updated</c>. Zatwierdzany jest tylko
/// wynik <see cref="OwnerWithdrawalChangeOutcome.Saved"/> (przebieg jak <c>ExpenseChangeRunner</c>).
/// </summary>
public sealed class EditOwnerWithdrawalHandler(IUnitOfWork unitOfWork, IAuditLog auditLog, IOwnerWithdrawalStore store, TimeProvider timeProvider)
    : ICommandHandler<EditOwnerWithdrawalCommand, OwnerWithdrawalChangeResult>
{
    public Task<OwnerWithdrawalChangeResult> HandleAsync(EditOwnerWithdrawalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Input, nameof(command));

        var errors = new FieldErrors();
        var version = OwnerWithdrawalVersion.Validate(command.Version, errors);
        var content = command.Input.Validate(errors, WarsawTime.Today(timeProvider));
        if (!errors.IsEmpty || version is null || content is null)
        {
            return Task.FromResult(OwnerWithdrawalChangeResult.Invalid(errors));
        }

        return unitOfWork.ExecuteInTransactionAsync(
            ct => EditAsync(command, version, content, ct),
            commitWhen: result => result.Outcome == OwnerWithdrawalChangeOutcome.Saved,
            cancellationToken);
    }

    private async Task<OwnerWithdrawalChangeResult> EditAsync(
        EditOwnerWithdrawalCommand command, byte[] version, OwnerWithdrawalContent content, CancellationToken cancellationToken)
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

        var changes = stored.Withdrawal.Edit(content, timeProvider.GetUtcNow().UtcDateTime);
        if (changes.Count == 0)
        {
            return new OwnerWithdrawalChangeResult(OwnerWithdrawalChangeOutcome.Saved, stored.Version);
        }

        var saved = await store.SaveAsync(stored.Withdrawal, version, cancellationToken);
        if (saved.Status == StoreSaveStatus.Conflict)
        {
            return new OwnerWithdrawalChangeResult(OwnerWithdrawalChangeOutcome.Conflict);
        }

        await auditLog.RecordAsync(
            OwnerWithdrawalAudit.Updated(stored.Withdrawal, changes, AuditAuthor.Account(command.AccountId)), cancellationToken);
        return new OwnerWithdrawalChangeResult(OwnerWithdrawalChangeOutcome.Saved, saved.Version);
    }
}
