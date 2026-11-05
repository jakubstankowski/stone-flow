using System.Runtime.ExceptionServices;
using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.Persistence;

namespace StoneFlow.Orders.Application.CreateOrder;

/// <summary>Dodanie zamówienia ręcznego; <paramref name="IdempotencyKey"/> z nagłówka <c>Idempotency-Key</c>.</summary>
public sealed record CreateOrderCommand(Guid AccountId, Guid IdempotencyKey, OrderInput Input) : ICommand<CreateOrderResult>;

public enum CreateOrderOutcome
{
    Created,

    /// <summary>Powtórzenie żądania z tym samym kluczem i tymi samymi danymi: istniejące zamówienie.</summary>
    Repeated,

    /// <summary>Ten sam klucz z innymi danymi: nowe zamówienie nie powstaje.</summary>
    KeyReused,

    ValidationFailed,
}

/// <summary>Wynik: <see cref="Version"/> tylko dla <c>Created</c> i <c>Repeated</c> (aktualna wersja).</summary>
public sealed record CreateOrderResult(
    CreateOrderOutcome Outcome,
    Guid? Id = null,
    string? Number = null,
    byte[]? Version = null,
    FieldErrors? Errors = null);

/// <summary>
/// W jednej jednostce pracy: szukanie zamówienia po kluczu, nadanie numeru, zapis i audyt
/// <c>Created</c>. Zatwierdzany jest wyłącznie wynik <c>Created</c>. Wyścig o ten sam klucz kończy
/// się naruszeniem unikalnego indeksu; po wycofaniu zamówienie jest czytane po kluczu na nowo.
/// </summary>
public sealed class CreateOrderHandler(
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    IOrderStore store,
    IOrderNumberAllocator numbers,
    IOrderReader reader,
    TimeProvider timeProvider) : ICommandHandler<CreateOrderCommand, CreateOrderResult>
{
    public async Task<CreateOrderResult> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Input, nameof(command));

        var errors = new FieldErrors();
        var content = command.Input.ValidateForCreate(errors);
        if (content is null)
        {
            return new CreateOrderResult(CreateOrderOutcome.ValidationFailed, Errors: errors);
        }

        var requestHash = OrderInput.ComputeRequestHash(content);
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
            // Naruszenie unikalności innego indeksu niż klucz idempotencji (np. numeru): błąd programu.
            ExceptionDispatchInfo.Throw(attempt.Duplicate!);
        }

        return existing.RequestHash.AsSpan().SequenceEqual(requestHash)
            ? new CreateOrderResult(CreateOrderOutcome.Repeated, existing.Id, existing.Number, existing.Version)
            : new CreateOrderResult(CreateOrderOutcome.KeyReused, existing.Id, existing.Number);
    }

    private async Task<Attempt> CreateAsync(
        CreateOrderCommand command,
        OrderContent content,
        byte[] requestHash,
        CancellationToken cancellationToken)
    {
        var existing = await reader.FindByCreationRequestAsync(command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return new Attempt(null, existing, null);
        }

        var now = timeProvider.GetUtcNow();
        var year = WarsawTime.Year(now);
        var number = Order.FormatNumber(year, await numbers.NextAsync(year, cancellationToken));
        var order = Order.CreateManual(Guid.NewGuid(), number, content, command.IdempotencyKey, requestHash, now.UtcDateTime);

        var saved = await store.AddAsync(order, cancellationToken);
        if (saved.Status != StoreSaveStatus.Saved)
        {
            return new Attempt(null, null, saved.Error ?? new InvalidOperationException("Nie udało się zapisać zamówienia."));
        }

        await auditLog.RecordAsync(OrderAudit.Created(order, command.AccountId), cancellationToken);
        return new Attempt(new CreateOrderResult(CreateOrderOutcome.Created, order.Id, order.Number, saved.Version), null, null);
    }

    private sealed record Attempt(CreateOrderResult? Created, CreationRequestMatch? Existing, Exception? Duplicate);
}
