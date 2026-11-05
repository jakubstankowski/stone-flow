using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Application.Payments;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Orders.Application.DeletePayment;

/// <summary>Usunięcie wpłaty (fizyczne, ślad w audycie); <paramref name="Version"/> wpisu (base64).</summary>
public sealed record DeletePaymentCommand(Guid AccountId, Guid OrderId, Guid EntryId, string? Version) : ICommand<EntryWriteResult>;

/// <summary>
/// Walidacja wersji (400), a potem w jednej jednostce pracy: blokada, wpis tego zamówienia (404),
/// wersja (409), reguła: Zapłacono po usunięciu nie może być ujemne (422), usunięcie i audyt
/// <c>Deleted</c> ze wszystkimi polami.
/// </summary>
public sealed class DeletePaymentHandler(OrderEntryWriter<Payment> writer) : ICommandHandler<DeletePaymentCommand, EntryWriteResult>
{
    public Task<EntryWriteResult> HandleAsync(DeletePaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new FieldErrors();
        if (EntryInput.ValidateVersion(command.Version, errors) is not { } version)
        {
            return Task.FromResult(EntryWriteResult.Invalid(errors));
        }

        return writer.DeleteAsync(
            command.AccountId,
            command.OrderId,
            command.EntryId,
            version,
            (payment, totals) => PaidRules.CheckPaymentChange(totals, payment.Amount, 0),
            cancellationToken);
    }
}
