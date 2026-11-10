using StoneFlow.Reporting.Domain.Periods;

namespace StoneFlow.Reporting.Domain.Costs;

/// <summary>Koszty kategorii.</summary>
public sealed record CategoryAmount(string Category, decimal Amount);

/// <summary>
/// Koszty okresu (SF-19, PRD §8): suma kwot brutto ze znakiem wszystkich wydatków z datą kosztu w
/// [efektywny początek, to] — opłaconych i nieopłaconych; wydatek ujemny je pomniejsza. Wydatki z
/// datą kosztu sprzed uruchomienia odpadają niezależnie od daty zapłaty. Podział na kategorie
/// (pełny słownik) i wartości w przedziałach wykresu, bez zaokrąglania.
/// </summary>
public sealed record CostSummary(decimal Costs, IReadOnlyList<CategoryAmount> Categories, IReadOnlyList<decimal> BucketCosts)
{
    /// <exception cref="ArgumentException">Wydatek z kategorią spoza <paramref name="categories"/> (błąd programu).</exception>
    public static CostSummary Of(IEnumerable<Cost> costs, ReportPeriod period, IReadOnlyList<string> categories)
    {
        ArgumentNullException.ThrowIfNull(costs);
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(categories);

        var byCategory = categories.ToDictionary(category => category, _ => 0m, StringComparer.Ordinal);
        var buckets = new decimal[period.Buckets().Count];
        var total = 0m;

        if (!period.BeforeLaunch)
        {
            var effective = period.Effective;
            foreach (var cost in costs.Where(c => effective.Contains(c.CostDate)))
            {
                if (!byCategory.TryGetValue(cost.Category, out var categoryCosts))
                {
                    throw new ArgumentException($"Kategoria „{cost.Category}” spoza słownika kategorii.", nameof(costs));
                }

                total += cost.GrossAmount;
                byCategory[cost.Category] = categoryCosts + cost.GrossAmount;
                buckets[period.BucketIndexOf(cost.CostDate)] += cost.GrossAmount;
            }
        }

        return new CostSummary(
            total,
            [.. categories.Select(category => new CategoryAmount(category, byCategory[category]))],
            buckets);
    }
}
