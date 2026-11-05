using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Application.Payments;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Orders.Application.AddPayment;

/// <summary>Dodanie wpłaty do zamówienia; <paramref name="IdempotencyKey"/> z nagłówka <c>Idempotency-Key</c>.</summary>
public sealed record AddPaymentCommand(Guid AccountId, Guid OrderId, Guid IdempotencyKey, PaymentInput Input) : ICommand<EntryWriteResult>;

/// <summary>
/// Walidacja (400), a potem w jednej jednostce pracy: blokada zamówienia, klucz idempotencji, zapis
/// i audyt. Wpłata zawsze zwiększa Zapłacono, więc nie ma reguły limitu. Zamówienie anulowane też
/// przyjmuje wpłatę (ostrzeżenie pokazuje UI).
/// </summary>
public sealed class AddPaymentHandler(OrderEntryWriter<Payment> writer, TimeProvider timeProvider)
    : ICommandHandler<AddPaymentCommand, EntryWriteResult>
{
    public Task<EntryWriteResult> HandleAsync(AddPaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Input, nameof(command));

        var errors = new FieldErrors();
        if (command.Input.Validate(errors, timeProvider.GetUtcNow()) is not { } details)
        {
            return Task.FromResult(EntryWriteResult.Invalid(errors));
        }

        var requestHash = PaymentInput.ComputeRequestHash(command.OrderId, details);
        return writer.AddAsync(
            command.AccountId,
            command.OrderId,
            command.IdempotencyKey,
            requestHash,
            check: null,
            nowUtc => Payment.Create(Guid.NewGuid(), command.OrderId, details, command.IdempotencyKey, requestHash, nowUtc),
            cancellationToken);
    }
}
