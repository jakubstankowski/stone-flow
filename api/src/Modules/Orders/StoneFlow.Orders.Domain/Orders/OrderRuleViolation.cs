namespace StoneFlow.Orders.Domain.Orders;

public enum OrderRuleViolationKind
{
    /// <summary>Zmiana pola należącego do sklepu w zamówieniu WooCommerce.</summary>
    FieldOwnedByShop,

    /// <summary>Status „Anulowane” zmieniają tylko akcje anulowania i przywracania.</summary>
    StatusChangeRequiresAction,

    AlreadyCancelled,

    NotCancelled,
}

/// <summary>Naruszenie reguły zamówienia; <see cref="Fields"/> tylko dla pól sklepu.</summary>
public sealed record OrderRuleViolation(OrderRuleViolationKind Kind, IReadOnlyList<ShopOwnedField> Fields)
{
    public static OrderRuleViolation StatusChangeRequiresAction { get; } = new(OrderRuleViolationKind.StatusChangeRequiresAction, []);

    public static OrderRuleViolation AlreadyCancelled { get; } = new(OrderRuleViolationKind.AlreadyCancelled, []);

    public static OrderRuleViolation NotCancelled { get; } = new(OrderRuleViolationKind.NotCancelled, []);

    public static OrderRuleViolation FieldOwnedByShop(IReadOnlyList<ShopOwnedField> fields) =>
        new(OrderRuleViolationKind.FieldOwnedByShop, fields);
}
