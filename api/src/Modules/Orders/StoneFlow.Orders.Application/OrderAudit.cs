using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Orders.Application;

/// <summary>
/// Wpisy audytu zamówienia (SF-7, konwencje „Value format”): etykieta to numer zamówienia, data
/// biznesowa to data zamówienia, pola klienta poza rodzajem są danymi osobowymi.
/// </summary>
public static class OrderAudit
{
    private static readonly JsonSerializerOptions ListJson = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>Utworzenie: nowe wartości pól, bez pól pustych; wartości logiczne zawsze.</summary>
    public static AuditEntry Created(Order order, Guid accountId) => Created(order, AuditAuthor.Account(accountId));

    /// <summary>Utworzenie z podanym autorem (np. „Synchronizacja WooCommerce”).</summary>
    public static AuditEntry Created(Order order, AuditAuthor author)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(author);

        var changes = order.Snapshot()
            .Select(field => (field.Field, Value: Format(field.Value)))
            .Where(field => field.Value is not null)
            .Select(field => Change(field.Field, null, field.Value))
            .ToList();

        return new AuditEntry(author, Object(order), AuditOperation.Created, changes);
    }

    /// <summary>Zmiana: tylko zmienione pola, wartości stare i nowe.</summary>
    public static AuditEntry Updated(Order order, IReadOnlyList<OrderFieldChange> changes, Guid accountId) =>
        Updated(order, changes, AuditAuthor.Account(accountId));

    /// <summary>Zmiana z podanym autorem (np. „Synchronizacja WooCommerce”).</summary>
    public static AuditEntry Updated(Order order, IReadOnlyList<OrderFieldChange> changes, AuditAuthor author)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(author);

        return new AuditEntry(
            author,
            Object(order),
            AuditOperation.Updated,
            [.. changes.Select(change => Change(change.Field, Format(change.OldValue), Format(change.NewValue)))]);
    }

    /// <summary>
    /// Wartość pola jako tekst: kwota <c>1234.50</c>, data <c>yyyy-MM-dd</c>, słownik jako kod,
    /// <c>true</c>/<c>false</c>, lista jako tablica JSON. Brak wartości i pusta lista to <c>null</c>.
    /// </summary>
    public static string? Format(object? value) => value switch
    {
        null => null,
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal amount => OrderInput.FormatValue(amount),
        bool flag => flag ? "true" : "false",
        OrderChannel channel => OrderCodes.Of(channel),
        FulfillmentStatus status => OrderCodes.Of(status),
        CustomerKind kind => OrderCodes.Of(kind),
        OrderSource source => OrderCodes.Of(source),
        PaymentMethod method => OrderCodes.Of(method),
        RefundMethod method => OrderCodes.Of(method),
        RefundKind kind => OrderCodes.Of(kind),
        IReadOnlyList<string> { Count: 0 } => null,
        IReadOnlyList<string> list => JsonSerializer.Serialize(list, ListJson),
        string text => text,
        _ => throw new ArgumentException($"Nieobsługiwany typ wartości: {value.GetType().Name}.", nameof(value)),
    };

    public static bool IsPersonalData(OrderField field) => field is OrderField.CustomerName
        or OrderField.CustomerPhone
        or OrderField.CustomerEmail
        or OrderField.CustomerStreet
        or OrderField.CustomerPostalCode
        or OrderField.CustomerCity
        or OrderField.CustomerTaxId;

    private static AuditChange Change(OrderField field, string? oldValue, string? newValue) =>
        new(OrderCodes.Of(field), oldValue, newValue, IsPersonalData(field));

    private static AuditObject Object(Order order) => new(AuditObjectType.Order, order.Id, order.OrderDate, order.Number);
}
