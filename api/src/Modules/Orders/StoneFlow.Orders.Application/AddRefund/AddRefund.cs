using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Application.Payments;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Orders.Application.AddRefund;

/// <summary>Dodanie zwrotu do zamówienia; <paramref name="IdempotencyKey"/> z nagłówka <c>Idempotency-Key</c>.</summary>
public sealed record AddRefundCommand(Guid AccountId, Guid OrderId, Guid IdempotencyKey, RefundInput Input) : ICommand<EntryWriteResult>;

/// <summary>
/// Walidacja (400), a potem w jednej jednostce pracy: blokada zamówienia, klucz idempotencji (przed
/// limitem), limit „zwrot ≤ Zapłacono” liczony pod blokadą (422 z <c>available</c>), zapis i audyt.
/// </summary>
public sealed class AddRefundHandler(OrderEntryWriter<Refund> writer, TimeProvider timeProvider)
    : ICommandHandler<AddRefundCommand, EntryWriteResult>
{
    public Task<EntryWriteResult> HandleAsync(AddRefundCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Input, nameof(command));

        var errors = new FieldErrors();
        if (command.Input.Validate(errors, timeProvider.GetUtcNow()) is not { } details)
        {
            return Task.FromResult(EntryWriteResult.Invalid(errors));
        }

        var requestHash = RefundInput.ComputeRequestHash(command.OrderId, details);
        return writer.AddAsync(
            command.AccountId,
            command.OrderId,
            command.IdempotencyKey,
            requestHash,
            totals => PaidRules.CheckRefund(totals, 0, details.Amount),
            nowUtc => Refund.Create(Guid.NewGuid(), command.OrderId, details, command.IdempotencyKey, requestHash, nowUtc),
            cancellationToken);
    }
}
