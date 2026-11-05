using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Application.Payments;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Orders.Application.DeleteRefund;

/// <summary>Usunięcie zwrotu (fizyczne, ślad w audycie); <paramref name="Version"/> wpisu (base64).</summary>
public sealed record DeleteRefundCommand(Guid AccountId, Guid OrderId, Guid EntryId, string? Version) : ICommand<EntryWriteResult>;

/// <summary>
/// Walidacja wersji (400), a potem w jednej jednostce pracy: blokada, wpis tego zamówienia (404),
/// wersja (409), usunięcie i audyt <c>Deleted</c>. Usunięcie zwrotu zawsze zwiększa Zapłacono, więc
/// nie ma reguły limitu.
/// </summary>
public sealed class DeleteRefundHandler(OrderEntryWriter<Refund> writer) : ICommandHandler<DeleteRefundCommand, EntryWriteResult>
{
    public Task<EntryWriteResult> HandleAsync(DeleteRefundCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new FieldErrors();
        if (EntryInput.ValidateVersion(command.Version, errors) is not { } version)
        {
            return Task.FromResult(EntryWriteResult.Invalid(errors));
        }

        return writer.DeleteAsync(command.AccountId, command.OrderId, command.EntryId, version, check: null, cancellationToken);
    }
}
