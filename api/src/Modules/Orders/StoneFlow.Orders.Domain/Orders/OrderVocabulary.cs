namespace StoneFlow.Orders.Domain.Orders;

/// <summary>Źródło techniczne zamówienia: wpisane w ERP albo pobrane ze sklepu.</summary>
public enum OrderSource
{
    Manual,
    WooCommerce,
}

/// <summary>Kanał sprzedaży (słownik stały w kodzie).</summary>
public enum OrderChannel
{
    /// <summary>„Sklep”.</summary>
    Shop,

    /// <summary>„OLX”.</summary>
    Olx,

    /// <summary>„Znajomy”.</summary>
    Acquaintance,

    /// <summary>„Telefon”.</summary>
    Phone,

    /// <summary>„Inne”.</summary>
    Other,
}

/// <summary>Status realizacji zamówienia (słownik stały w kodzie).</summary>
public enum FulfillmentStatus
{
    /// <summary>„W trakcie”.</summary>
    InProgress,

    /// <summary>„Zakończone”.</summary>
    Completed,

    /// <summary>„Problem/reklamacja”.</summary>
    Problem,

    /// <summary>„Anulowane”; ustawiane wyłącznie akcją anulowania.</summary>
    Cancelled,
}

/// <summary>Rodzaj klienta.</summary>
public enum CustomerKind
{
    Person,
    Company,
}

/// <summary>Pola zamówienia ze sklepu, których użytkownik nie zmienia w ERP.</summary>
public enum ShopOwnedField
{
    OrderDate,
    Value,
    Channel,
    Customer,
}

/// <summary>Pola zamówienia objęte audytem (kolejność jak w zapisie audytu).</summary>
public enum OrderField
{
    OrderDate,
    Channel,
    Value,
    FulfillmentStatus,
    CustomerKind,
    CustomerName,
    CustomerPhone,
    CustomerEmail,
    CustomerStreet,
    CustomerPostalCode,
    CustomerCity,
    CustomerTaxId,
    Suppliers,
    ZoReferences,
    InvoiceNumber,
    HasPhotos,
    HasReview,
    Notes,
    Comment,
}
