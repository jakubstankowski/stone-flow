using StoneFlow.Shared.Kernel.Operations;

namespace StoneFlow.Shared.Application.Operations;

/// <summary>
/// Źródło operacji pieniężnych modułu-właściciela (SF-17): Orders (wpłaty, zwroty) i Expenses
/// (opłacone wydatki, wypłaty właścicieli). Implementacje w Infrastructure modułów, rejestrowane
/// jako <c>IEnumerable&lt;ICashOperationSource&gt;</c> i wywoływane sekwencyjnie (wspólne połączenie
/// modułów finansowych, bez równoległych zapytań).
/// </summary>
public interface ICashOperationSource
{
    /// <summary>Rodzaje dostarczane przez to źródło (rozłączne z innymi źródłami).</summary>
    IReadOnlySet<CashOperationKind> Kinds { get; }

    /// <summary>
    /// Operacje żądanych rodzajów z datą w zakresie [<paramref name="from"/>, <paramref name="to"/>],
    /// najwyżej <paramref name="limit"/> wierszy łącznie (SQL <c>TOP</c>, porządek po dacie i id).
    /// </summary>
    /// <param name="from">Efektywny początek: już po uwzględnieniu daty uruchomienia.</param>
    /// <param name="to">Koniec okresu (włącznie).</param>
    /// <param name="kinds">Żądane rodzaje; rodzaje spoza <see cref="Kinds"/> są pomijane.</param>
    /// <param name="limit">Największa liczba wierszy (&gt; 0).</param>
    /// <param name="cancellationToken">Anulowanie zapytania.</param>
    Task<IReadOnlyList<CashOperation>> ListAsync(
        DateOnly from,
        DateOnly to,
        IReadOnlySet<CashOperationKind> kinds,
        int limit,
        CancellationToken cancellationToken);
}
