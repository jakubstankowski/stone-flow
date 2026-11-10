using Microsoft.EntityFrameworkCore;
using StoneFlow.Orders.Application;
using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Sales;
using StoneFlow.Shared.Kernel.Sales;

namespace StoneFlow.Orders.Infrastructure.Persistence;

/// <summary>
/// Składniki sprzedaży modułu Orders (SF-19): zamówienia według daty zamówienia, zwroty sprzedaży
/// (tylko rodzaj „Zwrot sprzedaży”, <see cref="Refund.SalesReductionOf"/>) i korekty sprzedaży
/// według ich dat. Dane zamówienia (numer, data, kanał, flaga) z bieżącego wiersza zamówienia, więc
/// zmiana kanału przenosi też obniżenia. Bez filtra statusu, anulowania ani daty uruchomienia w
/// SQL: flagę liczy po odczycie reguła domeny <see cref="SalesEligibility.CountsTowardSales"/>, a
/// przełom uruchomienia stosuje Reporting. Projekcje w SQL z limitem; zapytania idą po kolei
/// (wspólne połączenie modułów).
/// </summary>
internal sealed class OrderSalesComponentSource(OrdersDbContext context) : ISalesComponentSource
{
    public IReadOnlyList<string> Channels { get; } = [.. Enum.GetValues<OrderChannel>().Select(OrderCodes.Of)];

    public async Task<IReadOnlyList<SalesComponentRecord>> ListAsync(DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken)
    {
        var components = new List<SalesComponentRecord>();

        var orders = await context.Orders.AsNoTracking()
            .Where(o => o.OrderDate >= from && o.OrderDate <= to)
            .OrderBy(o => o.OrderDate)
            .ThenBy(o => o.Id)
            .Take(limit)
            .Select(o => new { o.Id, o.Number, o.OrderDate, o.Channel, o.Value, o.FulfillmentStatus, o.ShopStatus })
            .ToListAsync(cancellationToken);

        components.AddRange(orders.Select(o => new SalesComponentRecord(
            SalesComponentKind.Order,
            o.Id,
            o.OrderDate,
            o.Value,
            o.Id,
            o.Number,
            o.OrderDate,
            OrderCodes.Of(o.Channel),
            SalesEligibility.CountsTowardSales(o.FulfillmentStatus, o.ShopStatus))));

        // Limit wyczerpany: kolejne tabele nie są odpytywane (bez TOP 0).
        var remaining = limit - components.Count;
        if (remaining > 0)
        {
            var refunds = await context.Refunds.AsNoTracking()
                .Where(r => r.Kind == RefundKind.SalesReturn && r.Date >= from && r.Date <= to)
                .Join(context.Orders, r => r.OrderId, o => o.Id, (r, o) => new
                {
                    r.Id,
                    r.Date,
                    r.Amount,
                    r.OrderId,
                    o.Number,
                    o.OrderDate,
                    o.Channel,
                    o.FulfillmentStatus,
                    o.ShopStatus,
                })
                .OrderBy(r => r.Date)
                .ThenBy(r => r.Id)
                .Take(remaining)
                .ToListAsync(cancellationToken);

            components.AddRange(refunds.Select(r => new SalesComponentRecord(
                SalesComponentKind.SalesReturn,
                r.Id,
                r.Date,
                r.Amount,
                r.OrderId,
                r.Number,
                r.OrderDate,
                OrderCodes.Of(r.Channel),
                SalesEligibility.CountsTowardSales(r.FulfillmentStatus, r.ShopStatus))));
        }

        remaining = limit - components.Count;
        if (remaining > 0)
        {
            var corrections = await context.SalesCorrections.AsNoTracking()
                .Where(c => c.Date >= from && c.Date <= to)
                .Join(context.Orders, c => c.OrderId, o => o.Id, (c, o) => new
                {
                    c.Id,
                    c.Date,
                    c.Amount,
                    c.OrderId,
                    o.Number,
                    o.OrderDate,
                    o.Channel,
                    o.FulfillmentStatus,
                    o.ShopStatus,
                })
                .OrderBy(c => c.Date)
                .ThenBy(c => c.Id)
                .Take(remaining)
                .ToListAsync(cancellationToken);

            components.AddRange(corrections.Select(c => new SalesComponentRecord(
                SalesComponentKind.SalesCorrection,
                c.Id,
                c.Date,
                c.Amount,
                c.OrderId,
                c.Number,
                c.OrderDate,
                OrderCodes.Of(c.Channel),
                SalesEligibility.CountsTowardSales(c.FulfillmentStatus, c.ShopStatus))));
        }

        return components;
    }
}
