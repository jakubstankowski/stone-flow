using System.Runtime.ExceptionServices;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Domain.OwnerWithdrawals;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.Persistence;

namespace StoneFlow.Expenses.Application.CreateOwnerWithdrawal;

/// <summary>Dodanie wypłaty właściciela; <paramref name="IdempotencyKey"/> z nagłówka <c>Idempotency-Key</c>.</summary>
public sealed record CreateOwnerWithdrawalCommand(Guid AccountId, Guid IdempotencyKey, OwnerWithdrawalInput Input)
    : ICommand<CreateOwnerWithdrawalResult>;

public enum CreateOwnerWithdrawalOutcome
{
    Created,

    /// <summary>Powtórzenie żądania z tym samym kluczem i tymi samymi danymi: istniejąca wypłata.</summary>
    Repeated,

    /// <summary>Ten sam klucz z innymi danymi: nowa wypłata nie powstaje.</summary>
    KeyReused,

    ValidationFailed,
}

/// <summary>Wynik: <see cref="Version"/> tylko dla <c>Created</c> i <c>Repeated</c> (aktualna wersja).</summary>
public sealed record CreateOwnerWithdrawalResult(
    CreateOwnerWithdrawalOutcome Outcome, Guid? Id = null, byte[]? Version = null, FieldErrors? Errors = null);

/// <summary>
/// W jednej jednostce pracy: szukanie wypłaty po kluczu, zapis i audyt <c>Created</c>. Zatwierdzany
/// jest wyłącznie wynik <c>Created</c>. Wyścig o ten sam klucz kończy się naruszeniem unikalnego
/// indeksu; po wycofaniu wypłata jest czytana po kluczu na nowo (wzór <c>CreateExpenseHandler</c>).
/// </summary>
public sealed class CreateOwnerWithdrawalHandler(
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    IOwnerWithdrawalStore store,
    IOwnerWithdrawalReader reader,
    TimeProvider timeProvider) : ICommandHandler<CreateOwnerWithdrawalCommand, CreateOwnerWithdrawalResult>
{
    public async Task<CreateOwnerWithdrawalResult> HandleAsync(CreateOwnerWithdrawalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Input, nameof(command));

        var errors = new FieldErrors();
        var content = command.Input.Validate(errors, WarsawTime.Today(timeProvider));
        if (content is null)
        {
            return new CreateOwnerWithdrawalResult(CreateOwnerWithdrawalOutcome.ValidationFailed, Errors: errors);
        }

        var requestHash = OwnerWithdrawalInput.ComputeRequestHash(content);
        var attempt = await unitOfWork.ExecuteInTransactionAsync(
            ct => CreateAsync(command, content, requestHash, ct),
            commitWhen: result => result.Created is not null,
            cancellationToken);

        if (attempt.Created is { } created)
        {
            return created;
        }

        var existing = attempt.Existing
            ?? await reader.FindByCreationRequestAsync(command.IdempotencyKey, cancellationToken);
        if (existing is null)
        {
            // Naruszenie unikalności innego indeksu niż klucz idempotencji: błąd programu.
            ExceptionDispatchInfo.Throw(attempt.Duplicate!);
        }

        return existing.RequestHash.AsSpan().SequenceEqual(requestHash)
            ? new CreateOwnerWithdrawalResult(CreateOwnerWithdrawalOutcome.Repeated, existing.Id, existing.Version)
            : new CreateOwnerWithdrawalResult(CreateOwnerWithdrawalOutcome.KeyReused, existing.Id);
    }

    private async Task<Attempt> CreateAsync(
        CreateOwnerWithdrawalCommand command,
        OwnerWithdrawalContent content,
        byte[] requestHash,
        CancellationToken cancellationToken)
    {
        var existing = await reader.FindByCreationRequestAsync(command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return new Attempt(null, existing, null);
        }

        var withdrawal = OwnerWithdrawal.Create(
            Guid.NewGuid(), content, command.IdempotencyKey, requestHash, timeProvider.GetUtcNow().UtcDateTime);

        var saved = await store.AddAsync(withdrawal, cancellationToken);
        if (saved.Status != StoreSaveStatus.Saved)
        {
            return new Attempt(null, null, saved.Error ?? new InvalidOperationException("Nie udało się zapisać wypłaty."));
        }

        await auditLog.RecordAsync(OwnerWithdrawalAudit.Created(withdrawal, AuditAuthor.Account(command.AccountId)), cancellationToken);
        return new Attempt(
            new CreateOwnerWithdrawalResult(CreateOwnerWithdrawalOutcome.Created, withdrawal.Id, saved.Version), null, null);
    }

    private sealed record Attempt(CreateOwnerWithdrawalResult? Created, OwnerWithdrawalCreationMatch? Existing, Exception? Duplicate);
}
