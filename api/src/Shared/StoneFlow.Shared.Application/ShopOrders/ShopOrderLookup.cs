namespace StoneFlow.Shared.Application.ShopOrders;

/// <summary>Zamówienie ERP odpowiadające zamówieniu ze sklepu.</summary>
/// <param name="Id">Identyfikator zamówienia w ERP.</param>
/// <param name="Number">Numer zamówienia w ERP (nie jest daną osobową).</param>
public sealed record ShopOrderRef(Guid Id, string Number);

/// <summary>
/// Wyszukanie zamówień ERP po ID zamówienia w sklepie (SF-13, lista błędów synchronizacji);
/// implementacja w module zamówień.
/// </summary>
public interface IShopOrderLookup
{
    /// <summary>Zamówienia ze sklepu o podanych ID; ID bez zamówienia w ERP nie ma w wyniku.</summary>
    Task<IReadOnlyDictionary<string, ShopOrderRef>> FindAsync(IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken);
}
