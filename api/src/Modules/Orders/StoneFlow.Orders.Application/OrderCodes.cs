using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Orders.Domain.Payments;

namespace StoneFlow.Orders.Application;

/// <summary>
/// Stabilne kody słowników (camelCase) używane w API i w audycie. Frontend mapuje je na polskie
/// etykiety. Kody są porównywane dokładnie (z rozróżnieniem wielkości liter).
/// </summary>
public static class OrderCodes
{
    private static readonly Dictionary<OrderSource, string> Sources = new()
    {
        [OrderSource.Manual] = "manual",
        [OrderSource.WooCommerce] = "wooCommerce",
    };

    private static readonly Dictionary<OrderChannel, string> Channels = new()
    {
        [OrderChannel.Shop] = "shop",
        [OrderChannel.Olx] = "olx",
        [OrderChannel.Acquaintance] = "acquaintance",
        [OrderChannel.Phone] = "phone",
        [OrderChannel.Other] = "other",
    };

    private static readonly Dictionary<FulfillmentStatus, string> Statuses = new()
    {
        [FulfillmentStatus.InProgress] = "inProgress",
        [FulfillmentStatus.Completed] = "completed",
        [FulfillmentStatus.Problem] = "problem",
        [FulfillmentStatus.Cancelled] = "cancelled",
    };

    private static readonly Dictionary<CustomerKind, string> CustomerKinds = new()
    {
        [CustomerKind.Person] = "person",
        [CustomerKind.Company] = "company",
    };

    private static readonly Dictionary<ShopOwnedField, string> ShopFields = new()
    {
        [ShopOwnedField.OrderDate] = "orderDate",
        [ShopOwnedField.Value] = "value",
        [ShopOwnedField.Channel] = "channel",
        [ShopOwnedField.Customer] = "customer",
    };

    private static readonly Dictionary<PaymentMethod, string> PaymentMethods = new()
    {
        [PaymentMethod.Transfer] = "transfer",
        [PaymentMethod.Blik] = "blik",
        [PaymentMethod.Cash] = "cash",
        [PaymentMethod.CashOnDelivery] = "cashOnDelivery",
    };

    private static readonly Dictionary<RefundMethod, string> RefundMethods = new()
    {
        [RefundMethod.Transfer] = "transfer",
        [RefundMethod.Blik] = "blik",
        [RefundMethod.Cash] = "cash",
    };

    private static readonly Dictionary<RefundKind, string> RefundKinds = new()
    {
        [RefundKind.SalesReturn] = "salesReturn",
        [RefundKind.MoneyReturn] = "moneyReturn",
    };

    private static readonly Dictionary<PaymentState, string> PaymentStates = new()
    {
        [PaymentState.Unpaid] = "unpaid",
        [PaymentState.PartiallyPaid] = "partiallyPaid",
        [PaymentState.Paid] = "paid",
    };

    public static string Of(OrderSource source) => Sources[source];

    public static string Of(OrderChannel channel) => Channels[channel];

    public static string Of(FulfillmentStatus status) => Statuses[status];

    public static string Of(CustomerKind kind) => CustomerKinds[kind];

    /// <summary>Nazwa pola sklepu w żądaniu i w <c>shopOwnedFields</c>.</summary>
    public static string Of(ShopOwnedField field) => ShopFields[field];

    public static string Of(PaymentMethod method) => PaymentMethods[method];

    public static string Of(RefundMethod method) => RefundMethods[method];

    public static string Of(RefundKind kind) => RefundKinds[kind];

    public static string Of(PaymentState state) => PaymentStates[state];

    /// <summary>Nazwa pola w audycie: camelCase nazwy pola (<c>customerName</c>).</summary>
    public static string Of(OrderField field) => CamelCase(field.ToString());

    /// <summary>Kod ostrzeżenia ze sklepu (<c>activeInShop</c>, <c>deletedInShop</c>).</summary>
    public static string Of(ShopWarning warning) => CamelCase(warning.ToString());

    /// <summary>Nazwa pola wpisu w audycie (<c>amount</c>, <c>receivedBy</c>).</summary>
    public static string Of(EntryField field) => CamelCase(field.ToString());

    public static bool TryParse(string? code, out OrderSource source) => TryParse(Sources, code, out source);

    public static bool TryParse(string? code, out OrderChannel channel) => TryParse(Channels, code, out channel);

    public static bool TryParse(string? code, out FulfillmentStatus status) => TryParse(Statuses, code, out status);

    public static bool TryParse(string? code, out CustomerKind kind) => TryParse(CustomerKinds, code, out kind);

    public static bool TryParse(string? code, out PaymentMethod method) => TryParse(PaymentMethods, code, out method);

    public static bool TryParse(string? code, out RefundMethod method) => TryParse(RefundMethods, code, out method);

    public static bool TryParse(string? code, out RefundKind kind) => TryParse(RefundKinds, code, out kind);

    public static bool TryParse(string? code, out PaymentState state) => TryParse(PaymentStates, code, out state);

    private static string CamelCase(string name) => string.Concat(name[..1].ToLowerInvariant(), name.AsSpan(1));

    private static bool TryParse<TEnum>(Dictionary<TEnum, string> codes, string? code, out TEnum value)
        where TEnum : struct, Enum
    {
        foreach (var (key, text) in codes)
        {
            if (string.Equals(text, code, StringComparison.Ordinal))
            {
                value = key;
                return true;
            }
        }

        value = default;
        return false;
    }
}
