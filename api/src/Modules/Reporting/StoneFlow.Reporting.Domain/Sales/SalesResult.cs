namespace StoneFlow.Reporting.Domain.Sales;

/// <summary>
/// „Wynik przed wypłatami” (SF-19, PRD §8): sprzedaż minus koszty w tym samym okresie. Wypłaty
/// właścicieli go nie obniżają.
/// </summary>
public static class SalesResult
{
    public static decimal Of(decimal sales, decimal costs) => sales - costs;

    /// <summary>Wynik w każdym przedziale wykresu (te same przedziały sprzedaży i kosztów).</summary>
    /// <exception cref="ArgumentException">Różna liczba przedziałów.</exception>
    public static IReadOnlyList<decimal> Buckets(IReadOnlyList<decimal> salesBuckets, IReadOnlyList<decimal> costBuckets)
    {
        ArgumentNullException.ThrowIfNull(salesBuckets);
        ArgumentNullException.ThrowIfNull(costBuckets);
        if (salesBuckets.Count != costBuckets.Count)
        {
            throw new ArgumentException("Sprzedaż i koszty muszą mieć te same przedziały.", nameof(costBuckets));
        }

        return [.. salesBuckets.Select((sales, index) => Of(sales, costBuckets[index]))];
    }
}
