using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Orders.Application.GetOrder;

/// <summary>Wpłata w szczegółach zamówienia.</summary>
public sealed record PaymentEntryDto(
    Guid Id,
    decimal Amount,
    DateOnly Date,
    PaymentMethod Method,
    string? ReceivedBy,
    DateTime CreatedAtUtc,
    byte[] Version);

/// <summary>Zwrot w szczegółach zamówienia.</summary>
public sealed record RefundEntryDto(
    Guid Id,
    RefundKind Kind,
    decimal Amount,
    DateOnly Date,
    RefundMethod Method,
    string Reason,
    DateTime CreatedAtUtc,
    byte[] Version);

/// <summary>Korekta sprzedaży w szczegółach zamówienia.</summary>
public sealed record SalesCorrectionEntryDto(Guid Id, decimal Amount, DateOnly Date, string Reason, DateTime CreatedAtUtc, byte[] Version);

/// <summary>
/// Szczegóły zamówienia; czasy techniczne w UTC, <see cref="Version"/> to <c>rowversion</c>. Wpisy
/// są posortowane po dacie, czasie utworzenia i identyfikatorze (rosnąco).
/// </summary>
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
    byte[] Version,
    IReadOnlyList<PaymentEntryDto> Payments,
    IReadOnlyList<RefundEntryDto> Refunds,
    IReadOnlyList<SalesCorrectionEntryDto> SalesCorrections)
{
    /// <summary>Obniżenia sprzedaży i nadwyżka nad wartością (reguły w <see cref="SalesReductionRules"/>).</summary>
    public SalesReductionSummary SalesReductions => SalesReductionRules.Summarize(
        Value,
        Refunds.Where(refund => refund.Kind == RefundKind.SalesReturn).Sum(refund => refund.Amount),
        SalesCorrections.Sum(correction => correction.Amount));

    /// <summary>Podsumowanie płatności z wpisów zamówienia (reguły w <see cref="PaymentSummary"/>).</summary>
    public PaymentSummary Payment => PaymentSummary.Compute(
        Value,
        FulfillmentStatus == FulfillmentStatus.Cancelled,
        Payments.Sum(payment => payment.Amount),
        Refunds.Sum(refund => refund.Amount));

    /// <summary>Pola tylko do odczytu w edycji (puste dla zamówienia ręcznego).</summary>
    public IReadOnlyList<ShopOwnedField> ShopOwnedFields => Order.OwnedByShop(Source);

    /// <summary>Data modyfikacji w sklepie z ostatniej zastosowanej dostawy (SF-12).</summary>
    public DateTime? ShopModifiedAtUtc { get; init; }

    /// <summary>Usunięcie w sklepie (SF-12).</summary>
    public DateTime? ShopDeletedAtUtc { get; init; }

    /// <summary>Suma zwrotów w sklepie — informacja (SF-12).</summary>
    public decimal ShopRefundedAmount { get; init; }

    /// <summary>Ostrzeżenia ze sklepu (reguły w <see cref="ShopStatuses.WarningsFor"/>).</summary>
    public IReadOnlyList<ShopWarning> ShopWarnings =>
        ShopStatuses.WarningsFor(Source, FulfillmentStatus, ShopStatus, ShopDeletedAtUtc, ShopRefundedAmount);
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
