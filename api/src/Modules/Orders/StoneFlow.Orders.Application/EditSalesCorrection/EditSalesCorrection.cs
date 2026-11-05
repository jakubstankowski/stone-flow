using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Application.Payments;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Orders.Application.EditSalesCorrection;

/// <summary>Poprawa korekty sprzedaży; <paramref name="Version"/> wpisu (base64).</summary>
public sealed record EditSalesCorrectionCommand(Guid AccountId, Guid OrderId, Guid EntryId, SalesCorrectionInput Input, string? Version)
    : ICommand<EntryWriteResult>;

/// <summary>
/// Walidacja (400), a potem w jednej jednostce pracy: blokada, wpis tego zamówienia (404), wersja
/// (409), limit obniżeń dla starej i nowej kwoty (422 z <c>available</c>; poprawa, która nie
/// podnosi obniżeń, przechodzi także przy nadwyżce), zapis i audyt <c>Updated</c>.
/// </summary>
public sealed class EditSalesCorrectionHandler(OrderEntryWriter<SalesCorrection> writer, TimeProvider timeProvider)
    : ICommandHandler<EditSalesCorrectionCommand, EntryWriteResult>
{
    public Task<EntryWriteResult> HandleAsync(EditSalesCorrectionCommand command, CancellationToken cancellationToken)
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
            (correction, totals) => SalesReductionRules.Check(totals, correction.Amount, details.Amount),
            (correction, nowUtc) => correction.Edit(details, nowUtc),
            cancellationToken);
    }
}
