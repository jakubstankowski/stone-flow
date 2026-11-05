using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Application.Payments;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Orders.Application.AddSalesCorrection;

/// <summary>Dodanie korekty sprzedaży; <paramref name="IdempotencyKey"/> z nagłówka <c>Idempotency-Key</c>.</summary>
public sealed record AddSalesCorrectionCommand(Guid AccountId, Guid OrderId, Guid IdempotencyKey, SalesCorrectionInput Input)
    : ICommand<EntryWriteResult>;

/// <summary>
/// Walidacja (400), a potem w jednej jednostce pracy: blokada zamówienia (404), klucz
/// idempotencji, limit obniżeń sprzedaży pod blokadą (422 z <c>available</c>), zapis i audyt.
/// Korekta nie zmienia Zapłacono, więc reguła Zapłacono jej nie dotyczy.
/// </summary>
public sealed class AddSalesCorrectionHandler(OrderEntryWriter<SalesCorrection> writer, TimeProvider timeProvider)
    : ICommandHandler<AddSalesCorrectionCommand, EntryWriteResult>
{
    public Task<EntryWriteResult> HandleAsync(AddSalesCorrectionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Input, nameof(command));

        var errors = new FieldErrors();
        if (command.Input.Validate(errors, timeProvider.GetUtcNow()) is not { } details)
        {
            return Task.FromResult(EntryWriteResult.Invalid(errors));
        }

        var requestHash = SalesCorrectionInput.ComputeRequestHash(command.OrderId, details);
        return writer.AddAsync(
            command.AccountId,
            command.OrderId,
            command.IdempotencyKey,
            requestHash,
            totals => SalesReductionRules.Check(totals, 0, details.Amount),
            nowUtc => SalesCorrection.Create(Guid.NewGuid(), command.OrderId, details, command.IdempotencyKey, requestHash, nowUtc),
            cancellationToken);
    }
}
