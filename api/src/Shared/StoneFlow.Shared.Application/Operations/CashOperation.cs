using StoneFlow.Shared.Kernel.Operations;

namespace StoneFlow.Shared.Application.Operations;

/// <summary>
/// Operacja pieniężna z modułu-właściciela (projekcja w SQL, bez encji).
/// </summary>
/// <param name="Kind">Rodzaj operacji.</param>
/// <param name="Id">Identyfikator wpisu (wpłaty, zwrotu, wydatku albo wypłaty).</param>
/// <param name="Date">Data operacji pieniężnej (data wpłaty, zwrotu, zapłaty albo wypłaty).</param>
/// <param name="Amount">
/// Kwota zapisana: dodatnia dla wpłat, zwrotów i wypłat, ze znakiem dla wydatku. Kierunku nie
/// wyraża znak, tylko rodzaj.
/// </param>
/// <param name="Method">Kod metody; <c>null</c> dla wydatku bez metody.</param>
/// <param name="RefundKind">Kod rodzaju zwrotu (<c>salesReturn</c>, <c>moneyReturn</c>); tylko dla zwrotu.</param>
/// <param name="Description">Opis: numer zamówienia, opis wydatku albo osoba.</param>
/// <param name="Category">Kod kategorii; tylko dla wydatku.</param>
/// <param name="SourceId">Obiekt źródłowy: zamówienie dla wpłaty i zwrotu, w pozostałych przypadkach sam wpis.</param>
/// <param name="SourceLabel">Tekst linku do źródła.</param>
public sealed record CashOperation(
    CashOperationKind Kind,
    Guid Id,
    DateOnly Date,
    decimal Amount,
    string? Method,
    string? RefundKind,
    string? Description,
    string? Category,
    Guid SourceId,
    string SourceLabel);
