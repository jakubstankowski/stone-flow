namespace StoneFlow.Orders.Domain.Payments;

/// <summary>Metoda wpłaty (słownik stały w kodzie).</summary>
public enum PaymentMethod
{
    /// <summary>„Przelew”.</summary>
    Transfer,

    /// <summary>„BLIK”.</summary>
    Blik,

    /// <summary>„Gotówka”; tylko przy niej można podać „Przyjął”.</summary>
    Cash,

    /// <summary>„Pobranie”. To tylko metoda: zamówienie bez wpłaty pozostaje nieopłacone.</summary>
    CashOnDelivery,
}

/// <summary>Metoda zwrotu (bez pobrania, które jest sposobem przyjęcia pieniędzy od klienta).</summary>
public enum RefundMethod
{
    /// <summary>„Przelew”.</summary>
    Transfer,

    /// <summary>„BLIK”.</summary>
    Blik,

    /// <summary>„Gotówka”.</summary>
    Cash,
}

/// <summary>Rodzaj zwrotu: wpływ na sprzedaż liczą raporty (SF-19), limit obniżeń — SF-10.</summary>
public enum RefundKind
{
    /// <summary>„Zwrot sprzedaży”: oddany towar, wycofana sprzedaż.</summary>
    SalesReturn,

    /// <summary>„Zwrot pieniędzy”: zwrot nadpłaty lub zaliczki; nie zmienia sprzedaży.</summary>
    MoneyReturn,
}

/// <summary>Stan płatności zamówienia; zamówienie anulowane nie ma stanu.</summary>
public enum PaymentState
{
    /// <summary>„Nieopłacone”.</summary>
    Unpaid,

    /// <summary>„Częściowo opłacone”.</summary>
    PartiallyPaid,

    /// <summary>„Opłacone”.</summary>
    Paid,
}

/// <summary>Pola wpisów objęte audytem (kolejność jak w zapisie audytu).</summary>
public enum EntryField
{
    Kind,
    Amount,
    Date,
    Method,
    ReceivedBy,
    Reason,
}

/// <summary>Wartość pola wpisu objętego audytem.</summary>
public sealed record EntryFieldValue(EntryField Field, object? Value);

/// <summary>Zmienione pole wpisu: wartość stara i nowa.</summary>
public sealed record EntryFieldChange(EntryField Field, object? OldValue, object? NewValue);
