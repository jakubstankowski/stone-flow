namespace StoneFlow.Shared.Kernel.Sales;

/// <summary>
/// Rodzaj składnika sprzedaży (SF-19, widok „Sprzedaż”). Kolejność deklaracji to stała kolejność
/// remisu przy sortowaniu zestawienia po dacie.
/// </summary>
public enum SalesComponentKind
{
    /// <summary>„Zamówienie”: wartość brutto według daty zamówienia.</summary>
    Order,

    /// <summary>„Zwrot sprzedaży”: zwrot rodzaju „Zwrot sprzedaży” według daty zwrotu.</summary>
    SalesReturn,

    /// <summary>„Korekta sprzedaży” według daty korekty.</summary>
    SalesCorrection,
}
