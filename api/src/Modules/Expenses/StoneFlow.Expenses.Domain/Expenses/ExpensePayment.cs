namespace StoneFlow.Expenses.Domain.Expenses;

/// <summary>Stan wydatku i informacja „Po terminie” (dla szczegółów i pozycji listy).</summary>
public sealed record ExpensePaymentInfo(ExpensePaymentStatus Status, bool Overdue);

/// <summary>
/// Jedyna reguła stanu wydatku (PRD §5 „Zapłata wydatku”, PLAT-02). Stan wynika wyłącznie z daty
/// zapłaty; termin nie oznacza zapłaty. „Po terminie” dotyczy planowanego wydatku, którego termin
/// minął — w dniu terminu wydatek nie jest jeszcze po terminie.
/// </summary>
public static class ExpensePayment
{
    /// <param name="paidOn">Data zapłaty albo <c>null</c>.</param>
    /// <param name="dueDate">Termin płatności albo <c>null</c>.</param>
    /// <param name="today">Dzisiejsza data w Europe/Warsaw.</param>
    public static ExpensePaymentInfo Compute(DateOnly? paidOn, DateOnly? dueDate, DateOnly today) =>
        paidOn is not null
            ? new ExpensePaymentInfo(ExpensePaymentStatus.Paid, Overdue: false)
            : new ExpensePaymentInfo(ExpensePaymentStatus.Planned, dueDate is { } due && due < today);
}
