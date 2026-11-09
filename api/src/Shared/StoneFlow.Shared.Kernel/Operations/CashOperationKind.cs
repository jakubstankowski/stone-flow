namespace StoneFlow.Shared.Kernel.Operations;

/// <summary>
/// Rodzaj operacji pieniężnej (SF-17, lista „Operacje”; SF-20, przepływy pieniężne). Kolejność
/// deklaracji to stała kolejność remisu przy sortowaniu listy po dacie.
/// </summary>
public enum CashOperationKind
{
    /// <summary>„Wpłata” do zamówienia (Orders).</summary>
    Payment,

    /// <summary>„Zwrot sprzedaży” albo „Zwrot pieniędzy” do zamówienia (Orders).</summary>
    Refund,

    /// <summary>„Opłacony wydatek”: wydatek z datą zapłaty (Expenses).</summary>
    PaidExpense,

    /// <summary>„Wypłata właściciela” (Expenses).</summary>
    OwnerWithdrawal,
}
