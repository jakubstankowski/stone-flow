namespace StoneFlow.Orders.Domain.Orders;

/// <summary>
/// Czy stan zamówienia pozwala liczyć je do sprzedaży (PRD §8 „Sprzedaż”, SF-19). Jedyne miejsce tej
/// reguły: źródło składników sprzedaży liczy flagę tą metodą po odczycie, bez kopii w SQL.
/// </summary>
public static class SalesEligibility
{
    /// <summary>
    /// Liczy się zamówienie nieanulowane w ERP ze statusem ze sklepu spoza
    /// <see cref="ShopStatuses.HiddenByDefault"/> (pending, failed, checkout-draft). <c>null</c>
    /// (zamówienie ręczne) i pozostałe statusy — także nieznane, <c>on-hold</c>, <c>refunded</c> oraz
    /// <c>cancelled</c> w sklepie przy zamówieniu przywróconym w ERP — liczą się: decyduje status
    /// realizacji ERP. Filtr listy zamówień (<c>OrderReader</c>) to osobna reguła widoczności.
    /// </summary>
    public static bool CountsTowardSales(FulfillmentStatus fulfillment, string? shopStatus) =>
        fulfillment != FulfillmentStatus.Cancelled && !ShopStatuses.IsHiddenByDefault(shopStatus);
}
