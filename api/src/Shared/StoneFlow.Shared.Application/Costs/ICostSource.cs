namespace StoneFlow.Shared.Application.Costs;

/// <summary>
/// Źródło kosztów modułu Expenses (SF-19): wydatki według daty kosztu, opłacone i nieopłacone (także
/// wystąpienia cykliczne). Wypłaty właścicieli nie są kosztami i nie są zwracane. Implementacja w
/// Infrastructure modułu Expenses, jedna rejestracja scoped.
/// </summary>
public interface ICostSource
{
    /// <summary>Kody kategorii w kolejności prezentacji (słownik Expenses).</summary>
    IReadOnlyList<string> Categories { get; }

    /// <summary>
    /// Wszystkie wydatki z datą kosztu w zakresie [<paramref name="from"/>, <paramref name="to"/>],
    /// najwyżej <paramref name="limit"/> wierszy (SQL <c>TOP</c>, porządek po dacie kosztu i id).
    /// </summary>
    /// <param name="from">Początek zakresu (włącznie).</param>
    /// <param name="to">Koniec zakresu (włącznie).</param>
    /// <param name="limit">Największa liczba wierszy (&gt; 0).</param>
    /// <param name="cancellationToken">Anulowanie zapytania.</param>
    Task<IReadOnlyList<CostRecord>> ListAsync(DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken);
}
