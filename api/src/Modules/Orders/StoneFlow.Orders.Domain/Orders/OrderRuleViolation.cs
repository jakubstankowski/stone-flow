namespace StoneFlow.Orders.Domain.Orders;

public enum OrderRuleViolationKind
{
    /// <summary>Zmiana pola należącego do sklepu w zamówieniu WooCommerce.</summary>
    FieldOwnedByShop,

    /// <summary>Status „Anulowane” zmieniają tylko akcje anulowania i przywracania.</summary>
    StatusChangeRequiresAction,

    AlreadyCancelled,

    NotCancelled,

    /// <summary>Dane klienta zamówienia zostały już zanonimizowane (SF-22).</summary>
    AlreadyAnonymized,

    /// <summary>Zmiana klienta zamówienia zanonimizowanego (SF-22): danych nie można zmienić ani przywrócić.</summary>
    CustomerAnonymized,
}

/// <summary>Naruszenie reguły zamówienia; <see cref="Fields"/> tylko dla pól sklepu.</summary>
public sealed record OrderRuleViolation(OrderRuleViolationKind Kind, IReadOnlyList<ShopOwnedField> Fields)
{
    public static OrderRuleViolation StatusChangeRequiresAction { get; } = new(OrderRuleViolationKind.StatusChangeRequiresAction, []);

    public static OrderRuleViolation AlreadyCancelled { get; } = new(OrderRuleViolationKind.AlreadyCancelled, []);

    public static OrderRuleViolation NotCancelled { get; } = new(OrderRuleViolationKind.NotCancelled, []);

    public static OrderRuleViolation AlreadyAnonymized { get; } = new(OrderRuleViolationKind.AlreadyAnonymized, []);

    public static OrderRuleViolation CustomerAnonymized { get; } = new(OrderRuleViolationKind.CustomerAnonymized, []);

    public static OrderRuleViolation FieldOwnedByShop(IReadOnlyList<ShopOwnedField> fields) =>
        new(OrderRuleViolationKind.FieldOwnedByShop, fields);
}
