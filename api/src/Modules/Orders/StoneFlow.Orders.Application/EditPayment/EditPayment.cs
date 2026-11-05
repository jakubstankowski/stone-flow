using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Application.Payments;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Orders.Application.EditPayment;

/// <summary>Poprawa wpłaty; <paramref name="Version"/> wpisu odczytana przy wczytaniu (base64).</summary>
public sealed record EditPaymentCommand(Guid AccountId, Guid OrderId, Guid EntryId, PaymentInput Input, string? Version)
    : ICommand<EntryWriteResult>;

/// <summary>
/// Walidacja (400), a potem w jednej jednostce pracy: blokada, wpis tego zamówienia (404), wersja
/// (409), reguła: Zapłacono po poprawie nie może być ujemne (422, „najpierw popraw lub usuń
/// zwrot”), zapis i audyt <c>Updated</c>.
/// </summary>
public sealed class EditPaymentHandler(OrderEntryWriter<Payment> writer, TimeProvider timeProvider)
    : ICommandHandler<EditPaymentCommand, EntryWriteResult>
{
    public Task<EntryWriteResult> HandleAsync(EditPaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Input, nameof(command));

        var errors = new FieldErrors();
        var details = command.Input.Validate(errors, timeProvider.GetUtcNow());
        var version = EntryInput.ValidateVersion(command.Version, errors);
        if (!errors.IsEmpty || details is null || version is null)
        {
            return Task.FromResult(EntryWriteResult.Invalid(errors));
        }

        return writer.EditAsync(
            command.AccountId,
            command.OrderId,
            command.EntryId,
            version,
            (payment, totals) => PaidRules.CheckPaymentChange(totals, payment.Amount, details.Amount),
            (payment, nowUtc) => payment.Edit(details, nowUtc),
            cancellationToken);
    }
}
