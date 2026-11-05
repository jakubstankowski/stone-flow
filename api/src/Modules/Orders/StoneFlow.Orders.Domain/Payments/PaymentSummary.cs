namespace StoneFlow.Orders.Domain.Payments;

/// <summary>
/// Podsumowanie płatności zamówienia, liczone przy każdym odczycie (stan nie jest przechowywany).
/// Kwoty bez zaokrągleń: wpisy i wartość mają 2 miejsca po przecinku.
/// </summary>
/// <param name="Paid">„Zapłacono”: suma wpłat minus suma zwrotów.</param>
/// <param name="State">Stan płatności; <c>null</c> dla zamówienia anulowanego.</param>
/// <param name="AmountDue">„Do zapłaty”: tylko przy stanie Nieopłacone i Częściowo opłacone.</param>
/// <param name="Overpayment">„Nadpłata”: tylko dla zamówienia nieanulowanego, gdy Zapłacono przekracza wartość.</param>
/// <param name="ToRefund">„Do zwrotu”: tylko dla zamówienia anulowanego, gdy Zapłacono &gt; 0.</param>
public sealed record PaymentSummary(decimal Paid, PaymentState? State, decimal? AmountDue, decimal? Overpayment, decimal? ToRefund)
{
    /// <summary>
    /// Reguły (PRD, spec SF-9): zamówienie anulowane nie ma stanu i przy Zapłacono &gt; 0 ma „Do
    /// zwrotu”. Pozostałe: wartość 0 zł to zawsze Opłacone; Zapłacono ≤ 0 to Nieopłacone; mniej niż
    /// wartość to Częściowo opłacone; co najmniej wartość to Opłacone. Filtr listy w SQL
    /// (<c>OrderReader</c>) odwzorowuje te same progi.
    /// </summary>
    public static PaymentSummary Compute(decimal value, bool isCancelled, decimal paymentsTotal, decimal refundsTotal)
    {
        var paid = paymentsTotal - refundsTotal;
        if (isCancelled)
        {
            return new PaymentSummary(paid, null, null, null, paid > 0 ? paid : null);
        }

        var state = StateOf(value, paid);
        var amountDue = state is PaymentState.Unpaid or PaymentState.PartiallyPaid ? value - paid : (decimal?)null;
        var overpayment = paid > value ? paid - value : (decimal?)null;
        return new PaymentSummary(paid, state, amountDue, overpayment, null);
    }

    private static PaymentState StateOf(decimal value, decimal paid)
    {
        if (value == 0)
        {
            return PaymentState.Paid;
        }

        if (paid <= 0)
        {
            return PaymentState.Unpaid;
        }

        return paid < value ? PaymentState.PartiallyPaid : PaymentState.Paid;
    }
}

/// <summary>Sumy wpisów zamówienia (wpłat i zwrotów) policzone pod blokadą zamówienia.</summary>
public readonly record struct EntryTotals(decimal Payments, decimal Refunds)
{
    /// <summary>„Zapłacono”.</summary>
    public decimal Paid => Payments - Refunds;

    /// <summary>Sumy po zamianie kwoty wpłaty: dodanie (<paramref name="oldAmount"/> = 0), poprawa, usunięcie (<paramref name="newAmount"/> = 0).</summary>
    public EntryTotals ReplacingPayment(decimal oldAmount, decimal newAmount) => this with { Payments = Payments - oldAmount + newAmount };

    /// <summary>Sumy po zamianie kwoty zwrotu: dodanie (<paramref name="oldAmount"/> = 0), poprawa, usunięcie (<paramref name="newAmount"/> = 0).</summary>
    public EntryTotals ReplacingRefund(decimal oldAmount, decimal newAmount) => this with { Refunds = Refunds - oldAmount + newAmount };
}

public enum PaidRuleViolationKind
{
    /// <summary>Poprawa lub usunięcie wpłaty obniżyłyby Zapłacono poniżej 0 zł (najpierw zwrot).</summary>
    PaidWouldBeNegative,

    /// <summary>Zwrot przekroczyłby Zapłacono.</summary>
    RefundExceedsPaid,
}

/// <summary>Naruszenie reguły „Zapłacono po zmianie ≥ 0”; <see cref="Available"/> tylko dla zwrotu.</summary>
public sealed record PaidRuleViolation(PaidRuleViolationKind Kind, decimal? Available);

/// <summary>
/// Reguła limitu wpisów: po każdej zmianie Zapłacono nie może być ujemne. Dodanie wpłaty
/// i usunięcie zwrotu zawsze zwiększają Zapłacono, więc nie są sprawdzane.
/// </summary>
public static class PaidRules
{
    /// <summary>Poprawa (<paramref name="newAmount"/>) albo usunięcie (0) wpłaty o kwocie <paramref name="oldAmount"/>.</summary>
    public static PaidRuleViolation? CheckPaymentChange(EntryTotals totals, decimal oldAmount, decimal newAmount) =>
        totals.ReplacingPayment(oldAmount, newAmount).Paid < 0
            ? new PaidRuleViolation(PaidRuleViolationKind.PaidWouldBeNegative, null)
            : null;

    /// <summary>
    /// Dodanie (<paramref name="oldAmount"/> = 0) albo poprawa zwrotu. <c>Available</c> to kwota,
    /// do której zwrot może sięgnąć: Zapłacono bez tego zwrotu.
    /// </summary>
    public static PaidRuleViolation? CheckRefund(EntryTotals totals, decimal oldAmount, decimal newAmount) =>
        totals.ReplacingRefund(oldAmount, newAmount).Paid < 0
            ? new PaidRuleViolation(PaidRuleViolationKind.RefundExceedsPaid, Math.Max(0, totals.ReplacingRefund(oldAmount, 0).Paid))
            : null;
}
