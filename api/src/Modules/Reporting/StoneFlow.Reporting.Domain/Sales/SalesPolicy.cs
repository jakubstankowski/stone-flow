using StoneFlow.Shared.Kernel.Sales;

namespace StoneFlow.Reporting.Domain.Sales;

/// <summary>
/// Reguły sprzedaży (SF-19, PRD §8 „Sprzedaż”): który składnik wchodzi do sprzedaży i z jakim
/// znakiem. Jedna reguła sumy dla karty, kanałów, przedziałów wykresu i zestawienia.
/// </summary>
public static class SalesPolicy
{
    /// <summary>
    /// Składnik wchodzi do sprzedaży, gdy stan zamówienia pozwala je liczyć, zamówienie nie jest
    /// sprzed uruchomienia, a data składnika leży w [<paramref name="effectiveFrom"/>, <paramref name="to"/>].
    /// Anulowanie wyłącza więc wszystkie składniki zamówienia, także z wcześniejszych okresów.
    /// </summary>
    public static bool Includes(SalesComponent component, DateOnly effectiveFrom, DateOnly to, DateOnly launchDate)
    {
        ArgumentNullException.ThrowIfNull(component);
        return component.OrderCountsTowardSales
            && component.OrderDate >= launchDate
            && component.Date >= effectiveFrom
            && component.Date <= to;
    }

    /// <summary>Zamówienie: +wartość; zwrot sprzedaży i korekta sprzedaży: −kwota.</summary>
    public static decimal SignedAmount(SalesComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return component.Kind switch
        {
            SalesComponentKind.Order => component.Amount,
            SalesComponentKind.SalesReturn or SalesComponentKind.SalesCorrection => -component.Amount,
            _ => throw new ArgumentOutOfRangeException(nameof(component), component.Kind, "Nieznany rodzaj składnika."),
        };
    }

    /// <summary>Suma kwot ze znakiem, bez zaokrąglania.</summary>
    public static decimal SumOf(IEnumerable<SalesComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        return components.Sum(SignedAmount);
    }
}
