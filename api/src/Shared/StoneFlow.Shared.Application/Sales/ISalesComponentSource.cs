namespace StoneFlow.Shared.Application.Sales;

/// <summary>
/// Źródło składników sprzedaży modułu Orders (SF-19): zamówienia, zwroty sprzedaży i korekty
/// sprzedaży. Implementacja w Infrastructure modułu Orders, jedna rejestracja scoped. Reporting
/// wywołuje źródła po kolei (wspólne połączenie modułów finansowych).
/// </summary>
public interface ISalesComponentSource
{
    /// <summary>Kody kanałów w kolejności prezentacji (słownik Orders).</summary>
    IReadOnlyList<string> Channels { get; }

    /// <summary>
    /// Zamówienia z datą zamówienia w zakresie [<paramref name="from"/>, <paramref name="to"/>] oraz
    /// zwroty sprzedaży i korekty sprzedaży z datą w tym zakresie — także zamówień nieliczonych do
    /// sprzedaży (z flagą <see cref="SalesComponentRecord.OrderCountsTowardSales"/> = <c>false</c>), aby
    /// regułę wyłączenia stosował Reporting. Zwroty pieniędzy i wpłaty nie są zwracane. Najwyżej
    /// <paramref name="limit"/> wierszy łącznie (SQL <c>TOP</c>, tabelami po kolei, porządek po dacie i id).
    /// </summary>
    /// <param name="from">Początek zakresu (włącznie).</param>
    /// <param name="to">Koniec zakresu (włącznie).</param>
    /// <param name="limit">Największa liczba wierszy (&gt; 0).</param>
    /// <param name="cancellationToken">Anulowanie zapytania.</param>
    Task<IReadOnlyList<SalesComponentRecord>> ListAsync(DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken);
}
