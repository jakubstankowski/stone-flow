using Microsoft.EntityFrameworkCore;
using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Application.GetOrder;
using StoneFlow.Orders.Application.ListOrders;
using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Orders.Domain.Payments;

namespace StoneFlow.Orders.Infrastructure.Persistence;

/// <summary>
/// Odczyty zamówień: projekcje do DTO, filtrowanie, sortowanie i stronicowanie w bazie. Sumy wpisów
/// liczy baza (podzapytania <c>COALESCE(SUM(...), 0)</c>), a stan płatności — <see cref="PaymentSummary"/>.
/// </summary>
internal sealed class OrderReader(OrdersDbContext context) : IOrderReader
{
    private const string LikeEscape = "\\";

    public async Task<OrderListPageDto> ListAsync(OrderListCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var orders = Filter(context.Orders.AsNoTracking(), criteria, context.Payments, context.Refunds);
        var total = await orders.CountAsync(cancellationToken);

        var skip = (long)(criteria.Page - 1) * criteria.PageSize;
        if (skip >= total)
        {
            return new OrderListPageDto([], criteria.Page, criteria.PageSize, total);
        }

        var items = await Sort(orders, criteria)
            .Skip((int)skip)
            .Take(criteria.PageSize)
            .Select(o => new OrderListItemDto(
                o.Id,
                o.Number,
                o.OrderDate,
                o.Customer.Name,
                o.Channel,
                o.Source,
                o.Value,
                o.FulfillmentStatus,
                context.Payments.Where(p => p.OrderId == o.Id).Sum(p => p.Amount),
                context.Refunds.Where(r => r.OrderId == o.Id).Sum(r => r.Amount)))
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

        if (row is null)
        {
            return null;
        }

        var payments = await context.Payments.AsNoTracking()
            .Where(p => p.OrderId == id)
            .OrderBy(p => p.Date).ThenBy(p => p.CreatedAtUtc).ThenBy(p => p.Id)
            .Select(p => new PaymentEntryDto(
                p.Id,
                p.Amount,
                p.Date,
                p.Method,
                p.ReceivedBy,
                p.CreatedAtUtc,
                EF.Property<byte[]>(p, OrderEntryConfiguration.VersionProperty)))
            .ToListAsync(cancellationToken);

        var refunds = await context.Refunds.AsNoTracking()
            .Where(r => r.OrderId == id)
            .OrderBy(r => r.Date).ThenBy(r => r.CreatedAtUtc).ThenBy(r => r.Id)
            .Select(r => new RefundEntryDto(
                r.Id,
                r.Kind,
                r.Amount,
                r.Date,
                r.Method,
                r.Reason,
                r.CreatedAtUtc,
                EF.Property<byte[]>(r, OrderEntryConfiguration.VersionProperty)))
            .ToListAsync(cancellationToken);

        return new OrderDetailsDto(
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
                row.Version,
                [.. payments.Select(p => p with { CreatedAtUtc = DateTime.SpecifyKind(p.CreatedAtUtc, DateTimeKind.Utc) })],
                [.. refunds.Select(r => r with { CreatedAtUtc = DateTime.SpecifyKind(r.CreatedAtUtc, DateTimeKind.Utc) })]);
    }

    public async Task<CreationRequestMatch?> FindByCreationRequestAsync(Guid creationRequestId, CancellationToken cancellationToken)
    {
        var row = await context.Orders.AsNoTracking()
            .Where(o => o.CreationRequestId == creationRequestId)
            .Select(o => new { o.Id, o.Number, o.CreationRequestHash, Version = EF.Property<byte[]>(o, OrderConfiguration.VersionProperty) })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : new CreationRequestMatch(row.Id, row.Number, row.CreationRequestHash ?? [], row.Version);
    }

    private static IQueryable<Order> Filter(
        IQueryable<Order> orders, OrderListCriteria criteria, IQueryable<Payment> payments, IQueryable<Refund> refunds)
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

        if (criteria.PaymentStates.Count > 0 || criteria.WithoutPaymentState)
        {
            orders = FilterPaymentState(orders, criteria, payments, refunds);
        }

        if (criteria.Search is { } search)
        {
            // „Zawiera”, wielkość liter według collation bazy; %, _ i [ dosłownie.
            var pattern = "%" + EscapeLike(search) + "%";
            orders = orders.Where(o => EF.Functions.Like(o.Number, pattern, LikeEscape) || EF.Functions.Like(o.Customer.Name, pattern, LikeEscape));
        }

        return orders;
    }

    /// <summary>
    /// Filtr stanu płatności jako predykat SQL odpowiadający regułom <see cref="PaymentSummary.Compute"/>
    /// (zgodność sprawdza test listy ze szczegółami): anulowane — brak stanu; wartość 0 — Opłacone;
    /// Zapłacono ≤ 0 — Nieopłacone; Zapłacono &lt; wartość — Częściowo opłacone; pozostałe — Opłacone.
    /// Wybrane stany łączy OR.
    /// </summary>
    private static IQueryable<Order> FilterPaymentState(
        IQueryable<Order> orders, OrderListCriteria criteria, IQueryable<Payment> payments, IQueryable<Refund> refunds)
    {
        var none = criteria.WithoutPaymentState;
        var unpaid = criteria.PaymentStates.Contains(PaymentState.Unpaid);
        var partiallyPaid = criteria.PaymentStates.Contains(PaymentState.PartiallyPaid);
        var paid = criteria.PaymentStates.Contains(PaymentState.Paid);
        const FulfillmentStatus cancelled = FulfillmentStatus.Cancelled;

        return orders
            .Select(o => new
            {
                Order = o,
                Paid = payments.Where(p => p.OrderId == o.Id).Sum(p => p.Amount) - refunds.Where(r => r.OrderId == o.Id).Sum(r => r.Amount),
            })
            .Where(x =>
                (none && x.Order.FulfillmentStatus == cancelled)
                || (x.Order.FulfillmentStatus != cancelled
                    && ((paid && (x.Order.Value == 0 || x.Paid >= x.Order.Value))
                        || (unpaid && x.Order.Value != 0 && x.Paid <= 0)
                        || (partiallyPaid && x.Order.Value != 0 && x.Paid > 0 && x.Paid < x.Order.Value))))
            .Select(x => x.Order);
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
