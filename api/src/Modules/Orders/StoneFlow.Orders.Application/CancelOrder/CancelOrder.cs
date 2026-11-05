using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Orders.Application.CancelOrder;

/// <summary>„Anuluj zamówienie”: status „Anulowane” z kontrolą wersji i audytem.</summary>
public sealed record CancelOrderCommand(Guid AccountId, Guid OrderId, string? Version) : ICommand<OrderStatusActionResult>;

public sealed class CancelOrderHandler(OrderStatusAction statusAction) : ICommandHandler<CancelOrderCommand, OrderStatusActionResult>
{
    public Task<OrderStatusActionResult> HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return statusAction.ExecuteAsync(
            command.AccountId, command.OrderId, command.Version, (order, nowUtc) => order.Cancel(nowUtc), cancellationToken);
    }
}
