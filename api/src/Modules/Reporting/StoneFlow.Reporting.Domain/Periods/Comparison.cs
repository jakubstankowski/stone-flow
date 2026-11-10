namespace StoneFlow.Reporting.Domain.Periods;

/// <summary>Stan porównania karty z okresem bazowym.</summary>
public enum ComparisonStatus
{
    /// <summary>Porównanie dostępne: różnica i zmiana procentowa.</summary>
    Available,

    /// <summary>„Brak podstawy porównania”: baza równa zero albo zaczynająca się przed datą uruchomienia.</summary>
    NoBasis,
}

/// <summary>
/// Porównanie wartości karty z wartością okresu bazowego (SF-19). Różnica = bieżąca − bazowa,
/// zmiana procentowa = różnica / |bazowa| × 100 zaokrąglona do 0,1 połową od zera — jedyne
/// zaokrąglenie w raporcie. Wszystko na <c>decimal</c>, także dla liczby zamówień.
/// </summary>
public sealed record Comparison(ComparisonStatus Status, decimal? Base, decimal? Difference, decimal? Percent)
{
    /// <summary>„Brak podstawy porównania” bez wartości bazowej, różnicy i procentu.</summary>
    public static Comparison NoBasis { get; } = new(ComparisonStatus.NoBasis, null, null, null);

    public static Comparison Of(decimal current, decimal @base)
    {
        if (@base == 0m)
        {
            return NoBasis;
        }

        var difference = current - @base;
        var percent = Math.Round(difference / Math.Abs(@base) * 100m, 1, MidpointRounding.AwayFromZero);

        // Zero bez znaku: −0,04% to „0,0%”, nie „-0,0%”.
        return new Comparison(ComparisonStatus.Available, @base, difference, percent == 0m ? 0.0m : percent);
    }

    /// <summary>Wariant dla liczby zamówień: te same reguły na wartościach zamienionych na <c>decimal</c>.</summary>
    public static Comparison Of(int current, int @base) => Of((decimal)current, (decimal)@base);
}
