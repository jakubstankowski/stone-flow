using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Application.Payments;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Orders.Application.EditRefund;

/// <summary>Poprawa zwrotu, także rodzaju; <paramref name="Version"/> wpisu (base64).</summary>
public sealed record EditRefundCommand(Guid AccountId, Guid OrderId, Guid EntryId, RefundInput Input, string? Version)
    : ICommand<EntryWriteResult>;

/// <summary>
/// Walidacja (400), a potem w jednej jednostce pracy: blokada, wpis tego zamówienia (404), wersja
/// (409), limit: Zapłacono po poprawie nie może być ujemne (422 z <c>available</c>, czyli kwotą, do
/// której można podnieść zwrot), zapis i audyt <c>Updated</c>. Zmiana rodzaju nie sprawdza limitu
/// obniżeń sprzedaży (SF-10).
/// </summary>
public sealed class EditRefundHandler(OrderEntryWriter<Refund> writer, TimeProvider timeProvider)
    : ICommandHandler<EditRefundCommand, EntryWriteResult>
{
    public Task<EntryWriteResult> HandleAsync(EditRefundCommand command, CancellationToken cancellationToken)
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
            (refund, totals) => PaidRules.CheckRefund(totals, refund.Amount, details.Amount),
            (refund, nowUtc) => refund.Edit(details, nowUtc),
            cancellationToken);
    }
}
