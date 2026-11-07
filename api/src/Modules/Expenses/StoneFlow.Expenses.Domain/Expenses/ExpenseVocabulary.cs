namespace StoneFlow.Expenses.Domain.Expenses;

/// <summary>Kategoria kosztu (słownik stały w kodzie, PRD §7).</summary>
public enum ExpenseCategory
{
    /// <summary>„Zakup materiału u dostawców”.</summary>
    MaterialPurchase,

    /// <summary>„Cięcie”.</summary>
    Cutting,

    /// <summary>„Transport HDS”.</summary>
    HdsTransport,

    /// <summary>„Marketing” (Google Ads, OLX, SEO, media społecznościowe).</summary>
    Marketing,

    /// <summary>„Pozostała działalność”.</summary>
    OtherOperations,

    /// <summary>„Podatek VAT” — zwykły koszt; system nie wylicza zobowiązania.</summary>
    VatTax,

    /// <summary>„Podatek PPE” — zwykły koszt; system nie wylicza zobowiązania.</summary>
    PpeTax,
}

/// <summary>Metoda płatności wydatku (PRD §7). Metoda nie oznacza zapłaty.</summary>
public enum ExpensePaymentMethod
{
    /// <summary>„Przelew”.</summary>
    Transfer,

    /// <summary>„BLIK”.</summary>
    Blik,

    /// <summary>„Gotówka”.</summary>
    Cash,

    /// <summary>„Pobranie”.</summary>
    CashOnDelivery,
}

/// <summary>Stan wydatku wynikający wyłącznie z daty zapłaty.</summary>
public enum ExpensePaymentStatus
{
    /// <summary>„Planowany”: brak daty zapłaty, nie jest wypływem.</summary>
    Planned,

    /// <summary>„Opłacony”: data zapłaty uzupełniona.</summary>
    Paid,
}

/// <summary>Pola wydatku objęte audytem (kolejność jak w zapisie audytu).</summary>
public enum ExpenseField
{
    Description,
    Category,
    Counterparty,
    CostDate,
    DueDate,
    PaidOn,
    GrossAmount,
    NetAmount,
    DocumentReference,
    PaymentMethod,
    Payer,
    Comment,
}

/// <summary>Wartość pola objętego audytem (data, kwota, słownik albo tekst).</summary>
public sealed record ExpenseFieldValue(ExpenseField Field, object? Value);

/// <summary>Zmienione pole: wartość stara i nowa.</summary>
public sealed record ExpenseFieldChange(ExpenseField Field, object? OldValue, object? NewValue);

/// <summary>Naruszenie reguły akcji zapłaty.</summary>
public enum ExpenseRuleViolation
{
    /// <summary>„Oznacz jako opłacony” na wydatku, który ma już datę zapłaty.</summary>
    AlreadyPaid,

    /// <summary>„Cofnij oznaczenie zapłaty” na wydatku bez daty zapłaty.</summary>
    NotPaid,
}

/// <summary>Wynik zmiany wydatku: zmienione pola albo naruszenie reguły (bez zmian).</summary>
public sealed record ExpenseChangeSet(IReadOnlyList<ExpenseFieldChange> Changes, ExpenseRuleViolation? Violation = null)
{
    public bool Succeeded => Violation is null;

    public static ExpenseChangeSet Rejected(ExpenseRuleViolation violation) => new([], violation);
}
