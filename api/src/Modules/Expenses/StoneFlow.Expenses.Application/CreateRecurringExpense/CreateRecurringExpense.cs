using System.Runtime.ExceptionServices;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Application.Recurring;
using StoneFlow.Expenses.Domain.Recurring;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.Persistence;

namespace StoneFlow.Expenses.Application.CreateRecurringExpense;

/// <summary>Dodanie wydatku cyklicznego; <paramref name="IdempotencyKey"/> z nagłówka <c>Idempotency-Key</c>.</summary>
public sealed record CreateRecurringExpenseCommand(Guid AccountId, Guid IdempotencyKey, RecurringExpenseInput Input)
    : ICommand<CreateRecurringExpenseResult>;

public enum CreateRecurringExpenseOutcome
{
    Created,

    /// <summary>Powtórzenie z tym samym kluczem i danymi przy istniejącej definicji (liczba z pierwszego zapisu).</summary>
    Repeated,

    /// <summary>Ten sam klucz z innymi danymi (także po usunięciu definicji).</summary>
    KeyReused,

    /// <summary>Ten sam klucz i dane, ale definicję usunięto: nic nie powstaje.</summary>
    Deleted,

    ValidationFailed,
}

/// <summary>Wynik dodania; <see cref="Version"/> dla <c>Created</c> i <c>Repeated</c>.</summary>
public sealed record CreateRecurringExpenseResult(
    CreateRecurringExpenseOutcome Outcome,
    Guid? Id = null,
    byte[]? Version = null,
    int CreatedOccurrences = 0,
    FieldErrors? Errors = null);

/// <summary>
/// Walidacja (400, także „nie wypada żaden termin”), potem w jednej jednostce pracy: szukanie klucza
/// w <c>RecurringDefinitionRequests</c>, definicja, audyt <c>Created</c> z autorem konta,
/// <see cref="OccurrenceGenerator"/> na dzisiaj (wszystkie terminy od początku do dzisiaj włącznie)
/// i wiersz klucza. Zatwierdzany jest tylko wynik <c>Created</c>. Dodanie nie bierze blokady (nowej
/// definicji nikt nie widzi). Wyścig o klucz kończy się naruszeniem PK klucza; po wycofaniu klucz jest
/// czytany na nowo (wzór <c>CreateExpenseHandler</c>). Najpierw porównuje się skrót, niezależnie od
/// istnienia definicji.
/// </summary>
public sealed class CreateRecurringExpenseHandler(
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    IRecurringExpenseStore store,
    IRecurringExpenseReader reader,
    OccurrenceGenerator generator,
    TimeProvider timeProvider) : ICommandHandler<CreateRecurringExpenseCommand, CreateRecurringExpenseResult>
{
    public async Task<CreateRecurringExpenseResult> HandleAsync(CreateRecurringExpenseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Input, nameof(command));

        var errors = new FieldErrors();
        var input = command.Input.Validate(errors);
        if (input is not null && RecurringExpenseDefinition.CheckNew(input.Schedule) is not null)
        {
            errors.Add(RecurringFieldKeys.EndsOn, RecurringMessages.NoDueInRange);
            input = null;
        }

        if (input is null)
        {
            return new CreateRecurringExpenseResult(CreateRecurringExpenseOutcome.ValidationFailed, Errors: errors);
        }

        var requestHash = RecurringExpenseInput.ComputeRequestHash(input);
        var attempt = await unitOfWork.ExecuteInTransactionAsync(
            ct => CreateAsync(command, input, requestHash, ct),
            commitWhen: result => result.Created is not null,
            cancellationToken);

        if (attempt.Created is { } created)
        {
            return created;
        }

        var existing = attempt.Existing ?? await reader.FindRequestAsync(command.IdempotencyKey, cancellationToken);
        if (existing is null)
        {
            // Naruszenie innego klucza niż klucz idempotencji: błąd programu.
            ExceptionDispatchInfo.Throw(attempt.Duplicate!);
        }

        if (!existing.RequestHash.AsSpan().SequenceEqual(requestHash))
        {
            return new CreateRecurringExpenseResult(CreateRecurringExpenseOutcome.KeyReused, existing.DefinitionId);
        }

        var version = await reader.GetVersionAsync(existing.DefinitionId, cancellationToken);
        return version is null
            ? new CreateRecurringExpenseResult(CreateRecurringExpenseOutcome.Deleted, existing.DefinitionId)
            : new CreateRecurringExpenseResult(CreateRecurringExpenseOutcome.Repeated, existing.DefinitionId, version, existing.CreatedOccurrences);
    }

    private async Task<Attempt> CreateAsync(
        CreateRecurringExpenseCommand command, RecurringDefinitionInput input, byte[] requestHash, CancellationToken cancellationToken)
    {
        var existing = await reader.FindRequestAsync(command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return new Attempt(null, existing, null);
        }

        var now = timeProvider.GetUtcNow();
        var definition = RecurringExpenseDefinition.Create(Guid.NewGuid(), input.Content, input.Schedule, now.UtcDateTime);
        var saved = await store.AddAsync(definition, cancellationToken);
        if (saved.Status != StoreSaveStatus.Saved)
        {
            return new Attempt(null, null, saved.Error ?? new InvalidOperationException("Nie udało się zapisać wydatku cyklicznego."));
        }

        await auditLog.RecordAsync(RecurringExpenseAudit.Created(definition, AuditAuthor.Account(command.AccountId)), cancellationToken);

        var generation = await generator.GenerateAsync(definition, WarsawTime.Today(now), cancellationToken);
        if (generation.Status != OccurrenceGenerationStatus.Created)
        {
            // Nowa definicja ma nowe id, więc kolizja rejestru i brak definicji są niemożliwe.
            throw new InvalidOperationException("Nie udało się utworzyć wystąpień nowego wydatku cyklicznego.");
        }

        var request = RecurringDefinitionRequest.Create(command.IdempotencyKey, definition.Id, requestHash, generation.Created, now.UtcDateTime);
        var registered = await store.AddRequestAsync(request, cancellationToken);
        if (registered.Status != StoreSaveStatus.Saved)
        {
            return new Attempt(null, null, registered.Error ?? new InvalidOperationException("Nie udało się zapisać klucza żądania."));
        }

        return new Attempt(
            new CreateRecurringExpenseResult(CreateRecurringExpenseOutcome.Created, definition.Id, saved.Version, generation.Created),
            null,
            null);
    }

    private sealed record Attempt(CreateRecurringExpenseResult? Created, RecurringRequestMatch? Existing, Exception? Duplicate);
}
