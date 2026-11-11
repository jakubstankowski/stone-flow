using StoneFlow.Shared.Kernel.Sales;

namespace StoneFlow.Reporting.Domain.Sales;

/// <summary>
/// Składnik sprzedaży jako fakt raportu (SF-19): zamówienie, zwrot sprzedaży albo korekta sprzedaży
/// z danymi swojego zamówienia z bieżącego stanu.
/// </summary>
/// <param name="Kind">Rodzaj składnika.</param>
/// <param name="Id">Identyfikator wpisu.</param>
/// <param name="Date">Data składnika (zamówienia, zwrotu albo korekty).</param>
/// <param name="Amount">Kwota zapisana, nieujemna; znak nadaje <see cref="SalesPolicy.SignedAmount"/>.</param>
/// <param name="OrderId">Zamówienie składnika.</param>
/// <param name="OrderNumber">Numer zamówienia.</param>
/// <param name="OrderDate">Data zamówienia.</param>
/// <param name="Channel">Kod kanału zamówienia.</param>
/// <param name="OrderCountsTowardSales">Czy stan zamówienia pozwala liczyć je do sprzedaży (reguła Orders).</param>
public sealed record SalesComponent(
    SalesComponentKind Kind,
    Guid Id,
    DateOnly Date,
    decimal Amount,
    Guid OrderId,
    string OrderNumber,
    DateOnly OrderDate,
    string Channel,
    bool OrderCountsTowardSales);
