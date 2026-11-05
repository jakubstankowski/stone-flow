using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Orders.Application.RestoreOrder;

/// <summary>„Przywróć zamówienie”: anulowane wraca do statusu „W trakcie” z kontrolą wersji i audytem.</summary>
public sealed record RestoreOrderCommand(Guid AccountId, Guid OrderId, string? Version) : ICommand<OrderStatusActionResult>;

public sealed class RestoreOrderHandler(OrderStatusAction statusAction) : ICommandHandler<RestoreOrderCommand, OrderStatusActionResult>
{
    public Task<OrderStatusActionResult> HandleAsync(RestoreOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return statusAction.ExecuteAsync(
            command.AccountId, command.OrderId, command.Version, (order, nowUtc) => order.Restore(nowUtc), cancellationToken);
    }
}
