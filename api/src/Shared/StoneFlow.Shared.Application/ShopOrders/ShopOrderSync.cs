namespace StoneFlow.Shared.Application.ShopOrders;

/// <summary>Temat dostawy WooCommerce (SF-12).</summary>
public enum ShopOrderTopic
{
    Created,
    Updated,
    Restored,
    Deleted,
}

/// <summary>Rodzaj klienta zamówienia ze sklepu.</summary>
public enum ShopCustomerKind
{
    Person,
    Company,
}

/// <summary>Klient z dostawy; wartości surowe (przycięte), limity długości stosuje moduł zamówień.</summary>
public sealed record ShopCustomer(
    ShopCustomerKind Kind,
    string Name,
    string? Phone,
    string? Email,
    string? Street,
    string? PostalCode,
    string? City,
    string? TaxId);

/// <summary>
/// Zamówienie z dostawy ze sklepu. Dla <see cref="ShopOrderTopic.Deleted"/> wypełnione jest tylko
/// <see cref="ExternalId"/>; dla pozostałych tematów wszystkie pola poza klientem są wymagane.
/// </summary>
public sealed record ShopOrderSnapshot(
    string ExternalId,
    ShopOrderTopic Topic,
    DateTime? ModifiedAtUtc,
    DateOnly? OrderDate,
    string? Status,
    decimal? Value,
    decimal RefundedAmount,
    ShopCustomer? Customer)
{
    public static ShopOrderSnapshot Deletion(string externalId) =>
        new(externalId, ShopOrderTopic.Deleted, null, null, null, null, 0, null);
}

/// <summary>Wynik zastosowania dostawy.</summary>
public enum ShopOrderSyncResult
{
    Created,
    Updated,

    /// <summary>Dostawa zastosowana bez zmian (np. ponowienie).</summary>
    Unchanged,

    /// <summary>Starsza od zapisanych danych.</summary>
    SkippedOlder,

    /// <summary>Spóźniona dostawa sprzed usunięcia w sklepie.</summary>
    SkippedDeletedInShop,

    /// <summary>Zamówienie z datą sprzed uruchomienia: bez rekordu i bez błędu.</summary>
    SkippedBeforeLaunch,

    /// <summary>Usunięcie zamówienia, którego ERP jeszcze nie ma — zapamiętane.</summary>
    DeletionRecorded,

    /// <summary>
    /// Zapis równoczesny: naruszenie unikalności (dwie dostawy tworzące to samo zamówienie) albo
    /// konflikt wersji z zapisem użytkownika. Jednostka pracy wycofana; wywołujący ponawia raz
    /// w nowym zakresie DI na świeżych danych.
    /// </summary>
    RetryRequired,
}

/// <summary>Zastosowanie dostawy ze sklepu do zamówień ERP (implementacja w module zamówień).</summary>
public interface IShopOrderSync
{
    Task<ShopOrderSyncResult> ApplyAsync(ShopOrderSnapshot snapshot, CancellationToken cancellationToken);
}
