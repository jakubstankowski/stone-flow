using StoneFlow.Orders.Domain.Orders;

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

    public static string Of(OrderSource source) => Sources[source];

    public static string Of(OrderChannel channel) => Channels[channel];

    public static string Of(FulfillmentStatus status) => Statuses[status];

    public static string Of(CustomerKind kind) => CustomerKinds[kind];

    /// <summary>Nazwa pola sklepu w żądaniu i w <c>shopOwnedFields</c>.</summary>
    public static string Of(ShopOwnedField field) => ShopFields[field];

    /// <summary>Nazwa pola w audycie: camelCase nazwy pola (<c>customerName</c>).</summary>
    public static string Of(OrderField field)
    {
        var name = field.ToString();
        return string.Concat(name[..1].ToLowerInvariant(), name.AsSpan(1));
    }

    public static bool TryParse(string? code, out OrderSource source) => TryParse(Sources, code, out source);

    public static bool TryParse(string? code, out OrderChannel channel) => TryParse(Channels, code, out channel);

    public static bool TryParse(string? code, out FulfillmentStatus status) => TryParse(Statuses, code, out status);

    public static bool TryParse(string? code, out CustomerKind kind) => TryParse(CustomerKinds, code, out kind);

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
