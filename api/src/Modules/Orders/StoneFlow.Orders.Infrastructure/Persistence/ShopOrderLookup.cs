using Microsoft.EntityFrameworkCore;
using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Shared.Application.ShopOrders;

namespace StoneFlow.Orders.Infrastructure.Persistence;

/// <summary>Numer i Id zamówień ze sklepu po ID zewnętrznym (jedno zapytanie, bez śledzenia).</summary>
internal sealed class ShopOrderLookup(OrdersDbContext context) : IShopOrderLookup
{
    public async Task<IReadOnlyDictionary<string, ShopOrderRef>> FindAsync(
        IReadOnlyCollection<string> externalIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(externalIds);
        if (externalIds.Count == 0)
        {
            return new Dictionary<string, ShopOrderRef>(StringComparer.Ordinal);
        }

        var ids = externalIds.Distinct(StringComparer.Ordinal).ToList();
        var rows = await context.Orders
            .Where(o => o.Source == OrderSource.WooCommerce && o.ExternalId != null && ids.Contains(o.ExternalId))
            .Select(o => new { ExternalId = o.ExternalId!, o.Id, o.Number })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.ExternalId, r => new ShopOrderRef(r.Id, r.Number), StringComparer.Ordinal);
    }
}
