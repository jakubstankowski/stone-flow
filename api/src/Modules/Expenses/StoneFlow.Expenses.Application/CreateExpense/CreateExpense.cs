using System.Runtime.ExceptionServices;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Domain.Expenses;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.Persistence;

namespace StoneFlow.Expenses.Application.CreateExpense;

/// <summary>Dodanie wydatku; <paramref name="IdempotencyKey"/> z nagłówka <c>Idempotency-Key</c>.</summary>
public sealed record CreateExpenseCommand(Guid AccountId, Guid IdempotencyKey, ExpenseInput Input) : ICommand<CreateExpenseResult>;

public enum CreateExpenseOutcome
{
    Created,

    /// <summary>Powtórzenie żądania z tym samym kluczem i tymi samymi danymi: istniejący wydatek.</summary>
    Repeated,

    /// <summary>Ten sam klucz z innymi danymi: nowy wydatek nie powstaje.</summary>
    KeyReused,

    ValidationFailed,
}

/// <summary>Wynik: <see cref="Version"/> tylko dla <c>Created</c> i <c>Repeated</c> (aktualna wersja).</summary>
public sealed record CreateExpenseResult(CreateExpenseOutcome Outcome, Guid? Id = null, byte[]? Version = null, FieldErrors? Errors = null);

/// <summary>
/// W jednej jednostce pracy: szukanie wydatku po kluczu, zapis i audyt <c>Created</c>. Zatwierdzany
/// jest wyłącznie wynik <c>Created</c>. Wyścig o ten sam klucz kończy się naruszeniem unikalnego
/// indeksu; po wycofaniu wydatek jest czytany po kluczu na nowo (wzór <c>CreateOrderHandler</c>).
/// </summary>
public sealed class CreateExpenseHandler(
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    IExpenseStore store,
    IExpenseReader reader,
    TimeProvider timeProvider) : ICommandHandler<CreateExpenseCommand, CreateExpenseResult>
{
    public async Task<CreateExpenseResult> HandleAsync(CreateExpenseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Input, nameof(command));

        var errors = new FieldErrors();
        var content = command.Input.Validate(errors, WarsawTime.Today(timeProvider));
        if (content is null)
        {
            return new CreateExpenseResult(CreateExpenseOutcome.ValidationFailed, Errors: errors);
        }

        var requestHash = ExpenseInput.ComputeRequestHash(content);
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
            ? new CreateExpenseResult(CreateExpenseOutcome.Repeated, existing.Id, existing.Version)
            : new CreateExpenseResult(CreateExpenseOutcome.KeyReused, existing.Id);
    }

    private async Task<Attempt> CreateAsync(
        CreateExpenseCommand command,
        ExpenseContent content,
        byte[] requestHash,
        CancellationToken cancellationToken)
    {
        var existing = await reader.FindByCreationRequestAsync(command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return new Attempt(null, existing, null);
        }

        var expense = Expense.CreateManual(
            Guid.NewGuid(), content, command.IdempotencyKey, requestHash, timeProvider.GetUtcNow().UtcDateTime);

        var saved = await store.AddAsync(expense, cancellationToken);
        if (saved.Status != StoreSaveStatus.Saved)
        {
            return new Attempt(null, null, saved.Error ?? new InvalidOperationException("Nie udało się zapisać wydatku."));
        }

        await auditLog.RecordAsync(ExpenseAudit.Created(expense, AuditAuthor.Account(command.AccountId)), cancellationToken);
        return new Attempt(new CreateExpenseResult(CreateExpenseOutcome.Created, expense.Id, saved.Version), null, null);
    }

    private sealed record Attempt(CreateExpenseResult? Created, ExpenseCreationMatch? Existing, Exception? Duplicate);
}
