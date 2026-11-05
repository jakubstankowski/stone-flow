using Microsoft.EntityFrameworkCore;
using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Application.GetOrder;
using StoneFlow.Orders.Application.ListOrders;
using StoneFlow.Orders.Domain.Orders;

namespace StoneFlow.Orders.Infrastructure.Persistence;

/// <summary>Odczyty zamówień: projekcje do DTO, filtrowanie, sortowanie i stronicowanie w bazie.</summary>
internal sealed class OrderReader(OrdersDbContext context) : IOrderReader
{
    private const string LikeEscape = "\\";

    public async Task<OrderListPageDto> ListAsync(OrderListCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var orders = Filter(context.Orders.AsNoTracking(), criteria);
        var total = await orders.CountAsync(cancellationToken);

        var skip = (long)(criteria.Page - 1) * criteria.PageSize;
        if (skip >= total)
        {
            return new OrderListPageDto([], criteria.Page, criteria.PageSize, total);
        }

        var items = await Sort(orders, criteria)
            .Skip((int)skip)
            .Take(criteria.PageSize)
            .Select(o => new OrderListItemDto(o.Id, o.Number, o.OrderDate, o.Customer.Name, o.Channel, o.Source, o.Value, o.FulfillmentStatus))
            .ToListAsync(cancellationToken);

        return new OrderListPageDto(items, criteria.Page, criteria.PageSize, total);
    }

    public async Task<OrderDetailsDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await context.Orders.AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new
            {
                o.Id,
                o.Number,
                o.Source,
                o.ExternalId,
                o.ShopStatus,
                o.OrderDate,
                o.Channel,
                o.Value,
                o.FulfillmentStatus,
                o.Customer,
                o.Operational,
                o.Comment,
                o.CreatedAtUtc,
                o.UpdatedAtUtc,
                Version = EF.Property<byte[]>(o, OrderConfiguration.VersionProperty),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new OrderDetailsDto(
                row.Id,
                row.Number,
                row.Source,
                row.ExternalId,
                row.ShopStatus,
                row.OrderDate,
                row.Channel,
                row.Value,
                row.FulfillmentStatus,
                row.Customer,
                row.Operational,
                row.Comment,
                DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc),
                DateTime.SpecifyKind(row.UpdatedAtUtc, DateTimeKind.Utc),
                row.Version);
    }

    public async Task<CreationRequestMatch?> FindByCreationRequestAsync(Guid creationRequestId, CancellationToken cancellationToken)
    {
        var row = await context.Orders.AsNoTracking()
            .Where(o => o.CreationRequestId == creationRequestId)
            .Select(o => new { o.Id, o.Number, o.CreationRequestHash, Version = EF.Property<byte[]>(o, OrderConfiguration.VersionProperty) })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : new CreationRequestMatch(row.Id, row.Number, row.CreationRequestHash ?? [], row.Version);
    }

    private static IQueryable<Order> Filter(IQueryable<Order> orders, OrderListCriteria criteria)
    {
        if (criteria.DateFrom is { } from)
        {
            orders = orders.Where(o => o.OrderDate >= from);
        }

        if (criteria.DateTo is { } to)
        {
            orders = orders.Where(o => o.OrderDate <= to);
        }

        if (criteria.Channel is { } channel)
        {
            orders = orders.Where(o => o.Channel == channel);
        }

        if (criteria.Source is { } source)
        {
            orders = orders.Where(o => o.Source == source);
        }

        if (criteria.Statuses.Count > 0)
        {
            var statuses = criteria.Statuses;
            orders = orders.Where(o => statuses.Contains(o.FulfillmentStatus));
        }

        if (criteria.Search is { } search)
        {
            // „Zawiera”, wielkość liter według collation bazy; %, _ i [ dosłownie.
            var pattern = "%" + EscapeLike(search) + "%";
            orders = orders.Where(o => EF.Functions.Like(o.Number, pattern, LikeEscape) || EF.Functions.Like(o.Customer.Name, pattern, LikeEscape));
        }

        return orders;
    }

    private static IOrderedQueryable<Order> Sort(IQueryable<Order> orders, OrderListCriteria criteria) => (criteria.Sort, criteria.Descending) switch
    {
        (OrderSort.Number, false) => orders.OrderBy(o => o.Number).ThenBy(o => o.Id),
        (OrderSort.Number, true) => orders.OrderByDescending(o => o.Number).ThenByDescending(o => o.Id),
        (OrderSort.Customer, false) => orders.OrderBy(o => o.Customer.Name).ThenBy(o => o.Id),
        (OrderSort.Customer, true) => orders.OrderByDescending(o => o.Customer.Name).ThenByDescending(o => o.Id),
        (OrderSort.Value, false) => orders.OrderBy(o => o.Value).ThenBy(o => o.Id),
        (OrderSort.Value, true) => orders.OrderByDescending(o => o.Value).ThenByDescending(o => o.Id),
        (_, false) => orders.OrderBy(o => o.OrderDate).ThenBy(o => o.Id),
        (_, true) => orders.OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.Id),
    };

    private static string EscapeLike(string text) =>
        text.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal)
            .Replace("[", LikeEscape + "[", StringComparison.Ordinal);
}
