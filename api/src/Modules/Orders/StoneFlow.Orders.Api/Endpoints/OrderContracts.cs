using System.Globalization;
using StoneFlow.Orders.Application;
using StoneFlow.Orders.Application.GetOrder;
using StoneFlow.Orders.Application.ListOrders;
using StoneFlow.Orders.Domain.Payments;

namespace StoneFlow.Orders.Api.Endpoints;

// Kontrakt HTTP: kwoty jako tekst dziesiętny z kropką ("1234.50"), daty yyyy-MM-dd, słowniki jako
// kody camelCase (OrderCodes), wersja jako base64 z rowversion.

public sealed record CustomerRequest(
    string? Kind,
    string? Name,
    string? Phone,
    string? Email,
    string? Street,
    string? PostalCode,
    string? City,
    string? TaxId);

public sealed record OperationalRequest(
    IReadOnlyList<string?>? Suppliers,
    IReadOnlyList<string?>? ZoReferences,
    string? InvoiceNumber,
    bool HasPhotos,
    bool HasReview,
    string? Notes);

/// <summary>Ciało <c>POST</c> i <c>PUT</c>; <see cref="Version"/> tylko w <c>PUT</c>.</summary>
public sealed record OrderRequest(
    string? OrderDate,
    string? Channel,
    string? Value,
    string? FulfillmentStatus,
    CustomerRequest? Customer,
    OperationalRequest? Operational,
    string? Comment,
    string? Version = null);

/// <summary>Ciało <c>cancel</c> i <c>restore</c> zamówienia oraz <c>DELETE</c> wpisu (wersja wpisu).</summary>
public sealed record OrderVersionRequest(string? Version);

public sealed record OrderCreatedResponse(Guid Id, string Number, string Version);

public sealed record OrderVersionResponse(string Version);

public sealed record OrderListItemResponse(
    Guid Id,
    string Number,
    string OrderDate,
    string CustomerName,
    string Channel,
    string Source,
    string Value,
    string FulfillmentStatus,
    string Paid,
    string? PaymentState);

public sealed record OrderListPageResponse(IReadOnlyList<OrderListItemResponse> Items, int Page, int PageSize, int Total);

public sealed record CustomerResponse(
    string Kind,
    string Name,
    string? Phone,
    string? Email,
    string? Street,
    string? PostalCode,
    string? City,
    string? TaxId);

public sealed record OperationalResponse(
    IReadOnlyList<string> Suppliers,
    IReadOnlyList<string> ZoReferences,
    string? InvoiceNumber,
    bool HasPhotos,
    bool HasReview,
    string? Notes);

public sealed record OrderDetailsResponse(
    Guid Id,
    string Number,
    string Source,
    string? ExternalId,
    string? ShopStatus,
    string OrderDate,
    string Channel,
    string Value,
    string FulfillmentStatus,
    CustomerResponse Customer,
    OperationalResponse Operational,
    string? Comment,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string Version,
    IReadOnlyList<string> ShopOwnedFields,
    OrderPaymentSummaryResponse Payment,
    IReadOnlyList<PaymentEntryResponse> Payments,
    IReadOnlyList<RefundEntryResponse> Refunds,
    IReadOnlyList<SalesCorrectionEntryResponse> SalesCorrections,
    SalesReductionsResponse SalesReductions);

public sealed record SalesCorrectionEntryResponse(Guid Id, string Amount, string Date, string Reason, DateTime CreatedAtUtc, string Version);

/// <summary>Obniżenia sprzedaży: zwroty sprzedaży, korekty, suma i nadwyżka nad wartością (<c>null</c>, gdy jej nie ma).</summary>
public sealed record SalesReductionsResponse(string ReturnsTotal, string CorrectionsTotal, string Total, string? Excess);

/// <summary>Podsumowanie płatności; <see cref="State"/> <c>null</c> dla zamówienia anulowanego, kwoty opcjonalne jako <c>null</c>.</summary>
public sealed record OrderPaymentSummaryResponse(string Paid, string? State, string? AmountDue, string? Overpayment, string? ToRefund);

public sealed record PaymentEntryResponse(
    Guid Id,
    string Amount,
    string Date,
    string Method,
    string? ReceivedBy,
    DateTime CreatedAtUtc,
    string Version);

public sealed record RefundEntryResponse(
    Guid Id,
    string Kind,
    string Amount,
    string Date,
    string Method,
    string Reason,
    DateTime CreatedAtUtc,
    string Version);

/// <summary>Mapowanie kontraktu HTTP na wejście aplikacji i DTO na odpowiedzi.</summary>
internal static class OrderContracts
{
    public static OrderInput ToInput(OrderRequest? request) => request is null
        ? new OrderInput(null, null, null, null, null, null, null)
        : new OrderInput(
            request.OrderDate,
            request.Channel,
            request.Value,
            request.FulfillmentStatus,
            request.Customer is { } customer
                ? new CustomerInput(
                    customer.Kind,
                    customer.Name,
                    customer.Phone,
                    customer.Email,
                    customer.Street,
                    customer.PostalCode,
                    customer.City,
                    customer.TaxId)
                : null,
            request.Operational is { } operational
                ? new OperationalInput(
                    operational.Suppliers,
                    operational.ZoReferences,
                    operational.InvoiceNumber,
                    operational.HasPhotos,
                    operational.HasReview,
                    operational.Notes)
                : null,
            request.Comment);

    public static OrderListPageResponse ToResponse(OrderListPageDto page) => new(
        [
            .. page.Items.Select(item => new OrderListItemResponse(
                item.Id,
                item.Number,
                Date(item.OrderDate),
                item.CustomerName,
                OrderCodes.Of(item.Channel),
                OrderCodes.Of(item.Source),
                OrderInput.FormatValue(item.Value),
                OrderCodes.Of(item.FulfillmentStatus),
                OrderInput.FormatValue(item.Payment.Paid),
                item.Payment.State is { } state ? OrderCodes.Of(state) : null)),
        ],
        page.Page,
        page.PageSize,
        page.Total);

    public static OrderDetailsResponse ToResponse(OrderDetailsDto order) => new(
        order.Id,
        order.Number,
        OrderCodes.Of(order.Source),
        order.ExternalId,
        order.ShopStatus,
        Date(order.OrderDate),
        OrderCodes.Of(order.Channel),
        OrderInput.FormatValue(order.Value),
        OrderCodes.Of(order.FulfillmentStatus),
        new CustomerResponse(
            OrderCodes.Of(order.Customer.Kind),
            order.Customer.Name,
            order.Customer.Phone,
            order.Customer.Email,
            order.Customer.Street,
            order.Customer.PostalCode,
            order.Customer.City,
            order.Customer.TaxId),
        new OperationalResponse(
            order.Operational.Suppliers,
            order.Operational.ZoReferences,
            order.Operational.InvoiceNumber,
            order.Operational.HasPhotos,
            order.Operational.HasReview,
            order.Operational.Notes),
        order.Comment,
        order.CreatedAtUtc,
        order.UpdatedAtUtc,
        OrderVersion.Format(order.Version),
        [.. order.ShopOwnedFields.Select(OrderCodes.Of)],
        ToResponse(order.Payment),
        [
            .. order.Payments.Select(payment => new PaymentEntryResponse(
                payment.Id,
                OrderInput.FormatValue(payment.Amount),
                Date(payment.Date),
                OrderCodes.Of(payment.Method),
                payment.ReceivedBy,
                payment.CreatedAtUtc,
                OrderVersion.Format(payment.Version))),
        ],
        [
            .. order.Refunds.Select(refund => new RefundEntryResponse(
                refund.Id,
                OrderCodes.Of(refund.Kind),
                OrderInput.FormatValue(refund.Amount),
                Date(refund.Date),
                OrderCodes.Of(refund.Method),
                refund.Reason,
                refund.CreatedAtUtc,
                OrderVersion.Format(refund.Version))),
        ],
        [
            .. order.SalesCorrections.Select(correction => new SalesCorrectionEntryResponse(
                correction.Id,
                OrderInput.FormatValue(correction.Amount),
                Date(correction.Date),
                correction.Reason,
                correction.CreatedAtUtc,
                OrderVersion.Format(correction.Version))),
        ],
        new SalesReductionsResponse(
            OrderInput.FormatValue(order.SalesReductions.ReturnsTotal),
            OrderInput.FormatValue(order.SalesReductions.CorrectionsTotal),
            OrderInput.FormatValue(order.SalesReductions.Total),
            Amount(order.SalesReductions.Excess)));

    private static OrderPaymentSummaryResponse ToResponse(PaymentSummary summary) => new(
        OrderInput.FormatValue(summary.Paid),
        summary.State is { } state ? OrderCodes.Of(state) : null,
        Amount(summary.AmountDue),
        Amount(summary.Overpayment),
        Amount(summary.ToRefund));

    private static string? Amount(decimal? amount) => amount is { } value ? OrderInput.FormatValue(value) : null;

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
