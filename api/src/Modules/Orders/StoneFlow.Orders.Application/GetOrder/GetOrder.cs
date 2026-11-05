using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Orders.Application.GetOrder;

/// <summary>Szczegóły zamówienia; czasy techniczne w UTC, <see cref="Version"/> to <c>rowversion</c>.</summary>
public sealed record OrderDetailsDto(
    Guid Id,
    string Number,
    OrderSource Source,
    string? ExternalId,
    string? ShopStatus,
    DateOnly OrderDate,
    OrderChannel Channel,
    decimal Value,
    FulfillmentStatus FulfillmentStatus,
    Customer Customer,
    OperationalData Operational,
    string? Comment,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    byte[] Version)
{
    /// <summary>Pola tylko do odczytu w edycji (puste dla zamówienia ręcznego).</summary>
    public IReadOnlyList<ShopOwnedField> ShopOwnedFields => Order.OwnedByShop(Source);
}

public sealed record GetOrderQuery(Guid Id) : IQuery<OrderDetailsDto?>;

public sealed class GetOrderHandler(IOrderReader reader) : IQueryHandler<GetOrderQuery, OrderDetailsDto?>
{
    public Task<OrderDetailsDto?> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return reader.GetAsync(query.Id, cancellationToken);
    }
}
