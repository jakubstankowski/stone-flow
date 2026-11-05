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

/// <summary>
/// Sumy wpisów zamówienia policzone pod blokadą zamówienia: wpłaty i wszystkie zwroty (Zapłacono)
/// oraz, dla limitu obniżeń sprzedaży, wartość zamówienia, zwroty sprzedaży i korekty sprzedaży.
/// </summary>
public readonly record struct EntryTotals(decimal Payments, decimal Refunds)
{
    /// <summary>Wartość zamówienia <c>V</c>.</summary>
    public decimal Value { get; init; }

    /// <summary>Suma zwrotów rodzaju <see cref="RefundKind.SalesReturn"/>.</summary>
    public decimal SalesReturns { get; init; }

    /// <summary>Suma korekt sprzedaży.</summary>
    public decimal Corrections { get; init; }

    /// <summary>„Zapłacono”. Korekty nie są operacją pieniężną i go nie zmieniają.</summary>
    public decimal Paid => Payments - Refunds;

    /// <summary>Obniżenia sprzedaży <c>R</c>: zwroty sprzedaży i korekty.</summary>
    public decimal Reductions => SalesReturns + Corrections;

    /// <summary>Sumy po zamianie kwoty wpłaty: dodanie (<paramref name="oldAmount"/> = 0), poprawa, usunięcie (<paramref name="newAmount"/> = 0).</summary>
    public EntryTotals ReplacingPayment(decimal oldAmount, decimal newAmount) => this with { Payments = Payments - oldAmount + newAmount };

    /// <summary>Sumy po zamianie kwoty zwrotu: dodanie (<paramref name="oldAmount"/> = 0), poprawa, usunięcie (<paramref name="newAmount"/> = 0).</summary>
    public EntryTotals ReplacingRefund(decimal oldAmount, decimal newAmount) => this with { Refunds = Refunds - oldAmount + newAmount };
}

public enum EntryRuleViolationKind
{
    /// <summary>Obniżenia sprzedaży po zmianie przekroczyłyby wartość zamówienia (SF-10).</summary>
    SalesReductionLimitExceeded,

    /// <summary>Poprawa lub usunięcie wpłaty obniżyłyby Zapłacono poniżej 0 zł (najpierw zwrot).</summary>
    PaidWouldBeNegative,

    /// <summary>Zwrot przekroczyłby Zapłacono.</summary>
    RefundExceedsPaid,
}

/// <summary>
/// Naruszenie reguły wpisu: „Zapłacono po zmianie ≥ 0” (SF-9) albo limitu obniżeń sprzedaży
/// (SF-10). <see cref="Available"/>: przy zwrocie ponad Zapłacono — kwota, do której zwrot może
/// sięgnąć; przy limicie obniżeń — maksymalny wkład tego wpisu w obniżenia.
/// </summary>
public sealed record EntryRuleViolation(EntryRuleViolationKind Kind, decimal? Available);

/// <summary>
/// Reguła limitu wpisów: po każdej zmianie Zapłacono nie może być ujemne. Dodanie wpłaty
/// i usunięcie zwrotu zawsze zwiększają Zapłacono, więc nie są sprawdzane.
/// </summary>
public static class PaidRules
{
    /// <summary>Poprawa (<paramref name="newAmount"/>) albo usunięcie (0) wpłaty o kwocie <paramref name="oldAmount"/>.</summary>
    public static EntryRuleViolation? CheckPaymentChange(EntryTotals totals, decimal oldAmount, decimal newAmount) =>
        totals.ReplacingPayment(oldAmount, newAmount).Paid < 0
            ? new EntryRuleViolation(EntryRuleViolationKind.PaidWouldBeNegative, null)
            : null;

    /// <summary>
    /// Dodanie (<paramref name="oldAmount"/> = 0) albo poprawa zwrotu. <c>Available</c> to kwota,
    /// do której zwrot może sięgnąć: Zapłacono bez tego zwrotu.
    /// </summary>
    public static EntryRuleViolation? CheckRefund(EntryTotals totals, decimal oldAmount, decimal newAmount) =>
        totals.ReplacingRefund(oldAmount, newAmount).Paid < 0
            ? new EntryRuleViolation(EntryRuleViolationKind.RefundExceedsPaid, Math.Max(0, totals.ReplacingRefund(oldAmount, 0).Paid))
            : null;
}

/// <summary>Podsumowanie obniżeń sprzedaży; <see cref="Excess"/> = <c>R − V</c> tylko przy <c>R &gt; V</c>.</summary>
public sealed record SalesReductionSummary(decimal ReturnsTotal, decimal CorrectionsTotal, decimal Total, decimal? Excess);

/// <summary>
/// Limit obniżeń sprzedaży (PRD „Limit obniżeń sprzedaży”, SF-10): suma zwrotów sprzedaży
/// i korekt <c>R</c> nie może przekroczyć wartości zamówienia <c>V</c>. Jedna reguła dla korekt
/// i zwrotów (plan SF-10, N1): zmiana jest odrzucana wtedy i tylko wtedy, gdy podnosi <c>R</c>
/// i <c>R</c> po zmianie przekracza <c>V</c>. Zmiana, która nie podnosi <c>R</c>, przechodzi
/// także przy istniejącej nadwyżce, żeby dało się z niej wyjść. Obowiązuje też dla zamówień
/// anulowanych.
/// </summary>
public static class SalesReductionRules
{
    /// <summary>
    /// Zmiana wkładu wpisu w obniżenia z <paramref name="oldContribution"/> na
    /// <paramref name="newContribution"/> (dodanie: stary wkład 0; zwrot pieniędzy: wkład 0).
    /// <c>Available</c> = <c>max(V − (R − stary wkład), 0)</c>.
    /// </summary>
    public static EntryRuleViolation? Check(EntryTotals totals, decimal oldContribution, decimal newContribution)
    {
        var after = totals.Reductions - oldContribution + newContribution;
        return after > totals.Reductions && after > totals.Value
            ? new EntryRuleViolation(EntryRuleViolationKind.SalesReductionLimitExceeded, Math.Max(totals.Value - (totals.Reductions - oldContribution), 0))
            : null;
    }

    /// <summary>
    /// Czy ręczna zmiana wartości ją obniża — tylko taka zmiana podlega limitowi (i tylko wtedy
    /// <c>EditOrder</c> bierze blokadę zamówienia).
    /// </summary>
    public static bool LowersValue(decimal oldValue, decimal newValue) => newValue < oldValue;

    /// <summary>
    /// Ręczna zmiana wartości zamówienia: obniżenie poniżej sumy obniżeń jest odrzucane; wynik to
    /// minimalna dozwolona wartość (<c>R</c>), a <c>null</c> oznacza brak naruszenia. Podwyżka
    /// przechodzi zawsze, także przy istniejącej nadwyżce.
    /// </summary>
    public static decimal? MinimumValueFor(decimal oldValue, decimal newValue, decimal reductions) =>
        LowersValue(oldValue, newValue) && newValue < reductions ? reductions : null;

    public static SalesReductionSummary Summarize(decimal value, decimal salesReturnsTotal, decimal correctionsTotal)
    {
        var total = salesReturnsTotal + correctionsTotal;
        return new SalesReductionSummary(salesReturnsTotal, correctionsTotal, total, total > value ? total - value : null);
    }
}
