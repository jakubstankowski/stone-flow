using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Application.Payments;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Orders.Application.AddRefund;

/// <summary>Dodanie zwrotu do zamówienia; <paramref name="IdempotencyKey"/> z nagłówka <c>Idempotency-Key</c>.</summary>
public sealed record AddRefundCommand(Guid AccountId, Guid OrderId, Guid IdempotencyKey, RefundInput Input) : ICommand<EntryWriteResult>;

/// <summary>
/// Walidacja (400), a potem w jednej jednostce pracy: blokada zamówienia, klucz idempotencji (przed
/// limitem), limit „zwrot ≤ Zapłacono” liczony pod blokadą (422 <c>refund_exceeds_paid</c>), dla
/// zwrotu sprzedaży limit obniżeń (422 <c>sales_reduction_limit_exceeded</c>), zapis i audyt. Obie
/// reguły mają <c>available</c>; zwracana jest pierwsza naruszona.
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
            // Kolejność (SF-10): najpierw „zwrot ≤ Zapłacono”, potem limit obniżeń dla zwrotu sprzedaży.
            totals => PaidRules.CheckRefund(totals, 0, details.Amount) ?? SalesReductionRules.Check(totals, 0, details.SalesReduction),
            nowUtc => Refund.Create(Guid.NewGuid(), command.OrderId, details, command.IdempotencyKey, requestHash, nowUtc),
            cancellationToken);
    }
}
