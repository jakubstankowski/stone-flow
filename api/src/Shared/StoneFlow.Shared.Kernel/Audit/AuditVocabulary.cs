namespace StoneFlow.Shared.Kernel.Audit;

/// <summary>Rodzaj autora wpisu audytu: konto albo autor systemowy (PRD sekcja 3, D-05).</summary>
public enum AuditAuthorKind
{
    /// <summary>Zmiana wykonana przez użytkownika (jedyne konto).</summary>
    Account,

    /// <summary>„Synchronizacja WooCommerce”.</summary>
    WooCommerceSync,

    /// <summary>„Zadanie automatyczne”.</summary>
    AutomatedJob,
}

/// <summary>Rodzaje obiektów finansowych objętych audytem (PRD sekcja 5, „Historia zmian”).</summary>
public enum AuditObjectType
{
    Order,
    Payment,
    Refund,
    SalesCorrection,
    Expense,
    RecurringExpenseDefinition,
    OwnerWithdrawal,
}

public enum AuditOperation
{
    Created,
    Updated,
    Deleted,

    /// <summary>Anonimizacja danych klienta zamówienia (SF-22); zakres zmiany bez danych osobowych.</summary>
    Anonymized,
}
