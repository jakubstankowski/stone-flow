using StoneFlow.Shared.Kernel.Sales;

namespace StoneFlow.Shared.Application.Sales;

/// <summary>
/// Składnik sprzedaży z modułu Orders (projekcja w SQL, bez encji). Dane zamówienia pochodzą z jego
/// bieżącego stanu, także dla zwrotu i korekty.
/// </summary>
/// <param name="Kind">Rodzaj składnika.</param>
/// <param name="Id">Identyfikator wpisu (zamówienia, zwrotu albo korekty).</param>
/// <param name="Date">Data składnika: data zamówienia, zwrotu albo korekty.</param>
/// <param name="Amount">
/// Kwota zapisana, <b>nieujemna</b>: zamówienie może mieć 0 zł, zwrot i korekta są &gt; 0. Znak nadaje
/// Reporting.
/// </param>
/// <param name="OrderId">Zamówienie składnika.</param>
/// <param name="OrderNumber">Numer zamówienia.</param>
/// <param name="OrderDate">Data zamówienia.</param>
/// <param name="Channel">Kod kanału zamówienia z bieżącego stanu (<c>shop</c>, <c>olx</c>, …).</param>
/// <param name="OrderCountsTowardSales">
/// Czy bieżący stan zamówienia pozwala liczyć je do sprzedaży (reguła Orders: nie anulowane i status
/// ze sklepu spoza pending, failed, checkout-draft).
/// </param>
public sealed record SalesComponentRecord(
    SalesComponentKind Kind,
    Guid Id,
    DateOnly Date,
    decimal Amount,
    Guid OrderId,
    string OrderNumber,
    DateOnly OrderDate,
    string Channel,
    bool OrderCountsTowardSales);
