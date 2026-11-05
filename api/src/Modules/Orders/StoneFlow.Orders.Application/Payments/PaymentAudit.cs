using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Orders.Application.Payments;

/// <summary>
/// Wpisy audytu wpłat, zwrotów i korekt sprzedaży (SF-7): obiekt Payment, Refund albo
/// SalesCorrection z rodzicem Order, etykieta
/// „Wpłata do {numer}” / „Zwrot do {numer}” / „Korekta sprzedaży do {numer}” (bez danych
/// osobowych), data biznesowa to data wpisu.
/// Format wartości jak w <see cref="OrderAudit.Format"/>; „Przyjął” to dane osobowe.
/// </summary>
public static class PaymentAudit
{
    /// <summary>Utworzenie: nowe wartości pól, bez pól pustych.</summary>
    public static AuditEntry Created(OrderEntry entry, string orderNumber, Guid accountId)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var changes = entry.Snapshot()
            .Select(field => (field.Field, Value: OrderAudit.Format(field.Value)))
            .Where(field => field.Value is not null)
            .Select(field => Change(field.Field, null, field.Value))
            .ToList();

        return new AuditEntry(AuditAuthor.Account(accountId), Object(entry, orderNumber), AuditOperation.Created, changes);
    }

    /// <summary>Poprawa: tylko zmienione pola; data biznesowa to nowa data wpisu.</summary>
    public static AuditEntry Updated(OrderEntry entry, IReadOnlyList<EntryFieldChange> changes, string orderNumber, Guid accountId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(changes);

        return new AuditEntry(
            AuditAuthor.Account(accountId),
            Object(entry, orderNumber),
            AuditOperation.Updated,
            [.. changes.Select(change => Change(change.Field, OrderAudit.Format(change.OldValue), OrderAudit.Format(change.NewValue)))]);
    }

    /// <summary>
    /// Usunięcie (fizyczne): wszystkie niepuste pola jako stare wartości z nową <c>null</c>, aby
    /// ślad pozostał po zniknięciu wiersza.
    /// </summary>
    public static AuditEntry Deleted(OrderEntry entry, string orderNumber, Guid accountId)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var changes = entry.Snapshot()
            .Select(field => (field.Field, Value: OrderAudit.Format(field.Value)))
            .Where(field => field.Value is not null)
            .Select(field => Change(field.Field, field.Value, null))
            .ToList();

        return new AuditEntry(AuditAuthor.Account(accountId), Object(entry, orderNumber), AuditOperation.Deleted, changes);
    }

    public static bool IsPersonalData(EntryField field) => field == EntryField.ReceivedBy;

    /// <summary>Etykieta wpisu: „Wpłata do …”, „Zwrot do …” albo „Korekta sprzedaży do …” z numerem zamówienia.</summary>
    public static string Label(OrderEntry entry, string orderNumber) => $"{Kind(entry).Name} do {orderNumber}";

    /// <summary>Typ obiektu audytu z rodzaju wpisu (plan SF-10, N5).</summary>
    public static AuditObjectType ObjectType(OrderEntry entry) => Kind(entry).Type;

    private static (AuditObjectType Type, string Name) Kind(OrderEntry entry) => entry switch
    {
        Payment => (AuditObjectType.Payment, "Wpłata"),
        Refund => (AuditObjectType.Refund, "Zwrot"),
        SalesCorrection => (AuditObjectType.SalesCorrection, "Korekta sprzedaży"),
        _ => throw new ArgumentException($"Nieobsługiwany wpis: {entry?.GetType().Name}.", nameof(entry)),
    };

    private static AuditChange Change(EntryField field, string? oldValue, string? newValue) =>
        new(OrderCodes.Of(field), oldValue, newValue, IsPersonalData(field));

    private static AuditObject Object(OrderEntry entry, string orderNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderNumber);
        return new AuditObject(ObjectType(entry), entry.Id, entry.Date, Label(entry, orderNumber), new AuditObjectReference(AuditObjectType.Order, entry.OrderId));
    }
}
