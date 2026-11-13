using System.Globalization;
using StoneFlow.Orders.Application.Payments;
using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Orders.Application;

/// <summary>
/// Audyt anonimizacji danych klienta (DANE-01, SF-22): pola zastępowane znacznikiem we wpisach zamówienia
/// i jego wpisów oraz wpisy operacji <see cref="AuditOperation.Anonymized"/> bez danych osobowych.
/// Wpisy operacji zapisuje się po <see cref="IAuditRedaction.RedactAsync"/>, żeby nie podlegały anonimizacji.
/// </summary>
public static class OrderAnonymizationAudit
{
    /// <summary>
    /// Pola do anonimizacji w audycie: klient zamówienia (bez rodzaju), uwagi do realizacji, komentarz oraz
    /// powód zwrotu i korekty sprzedaży. Nazwy jak w zapisanych wpisach (<see cref="OrderAudit"/>, <see cref="PaymentAudit"/>).
    /// </summary>
    public static IReadOnlyList<AuditFieldSelector> Fields { get; } =
    [
        .. new[]
        {
            OrderField.CustomerName,
            OrderField.CustomerPhone,
            OrderField.CustomerEmail,
            OrderField.CustomerStreet,
            OrderField.CustomerPostalCode,
            OrderField.CustomerCity,
            OrderField.CustomerTaxId,
            OrderField.Notes,
            OrderField.Comment,
        }.Select(field => new AuditFieldSelector(AuditObjectType.Order, OrderCodes.Of(field))),
        new AuditFieldSelector(AuditObjectType.Refund, OrderCodes.Of(EntryField.Reason)),
        new AuditFieldSelector(AuditObjectType.SalesCorrection, OrderCodes.Of(EntryField.Reason)),
    ];

    /// <summary>Anonimizacja wpisów zamówienia i jego wpłat, zwrotów i korekt (także wpisów usunięcia).</summary>
    public static AuditRedaction Redaction(Guid orderId) =>
        new(new AuditObjectReference(AuditObjectType.Order, orderId), Fields, CustomerAnonymization.Marker);

    /// <summary>
    /// Wpis operacji zamówienia: <c>anonymizedFields</c> (tablica JSON kodów pól wyczyszczonych przez
    /// operację; pusta to <c>[]</c>), <c>anonymizedReasons</c> (liczba zwrotów i korekt ze zmienionym powodem),
    /// <c>retentionStartDate</c> (<c>yyyy-MM-dd</c>) i <c>retentionMonths</c>; bez starych wartości i danych osobowych.
    /// </summary>
    public static AuditEntry Anonymized(
        Order order,
        IReadOnlyList<OrderField> clearedFields,
        int anonymizedReasons,
        DateOnly retentionStart,
        int retentionMonths,
        Guid accountId)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(clearedFields);

        return new AuditEntry(
            AuditAuthor.Account(accountId),
            new AuditObject(AuditObjectType.Order, order.Id, order.OrderDate, order.Number),
            AuditOperation.Anonymized,
            [
                Change("anonymizedFields", FieldList([.. clearedFields.Select(OrderCodes.Of)])),
                Change("anonymizedReasons", anonymizedReasons.ToString(CultureInfo.InvariantCulture)),
                Change("retentionStartDate", OrderAudit.Format(retentionStart)!),
                Change("retentionMonths", retentionMonths.ToString(CultureInfo.InvariantCulture)),
            ]);
    }

    /// <summary>
    /// Wpis operacji zwrotu albo korekty ze zmienionym powodem: obiekt i etykieta jak w <see cref="PaymentAudit"/>,
    /// rodzic zamówienie, jedno pole <c>anonymizedFields</c> = <c>["reason"]</c>.
    /// </summary>
    public static AuditEntry EntryAnonymized(OrderEntry entry, string orderNumber, Guid accountId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderNumber);

        return new AuditEntry(
            AuditAuthor.Account(accountId),
            new AuditObject(
                PaymentAudit.ObjectType(entry),
                entry.Id,
                entry.Date,
                PaymentAudit.Label(entry, orderNumber),
                new AuditObjectReference(AuditObjectType.Order, entry.OrderId)),
            AuditOperation.Anonymized,
            [Change("anonymizedFields", FieldList([OrderCodes.Of(EntryField.Reason)]))]);
    }

    private static string FieldList(IReadOnlyList<string> codes) => OrderAudit.Format(codes) ?? "[]";

    private static AuditChange Change(string field, string value) => new(field, null, value, IsPersonalData: false);
}
