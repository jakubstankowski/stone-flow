namespace StoneFlow.Reporting.Domain.Periods;

/// <summary>Wybór okresu raportu.</summary>
public enum PeriodKind
{
    /// <summary>„Bieżący miesiąc”: cały miesiąc kalendarzowy dzisiejszej daty.</summary>
    CurrentMonth,

    /// <summary>„Poprzedni miesiąc”.</summary>
    PreviousMonth,

    /// <summary>„Rok”: bieżący do dziś albo wcześniejszy w całości.</summary>
    Year,

    /// <summary>„Własny zakres”.</summary>
    Custom,
}

/// <summary>Agregacja wykresu w czasie.</summary>
public enum Granularity
{
    /// <summary>Przedziały dzienne.</summary>
    Day,

    /// <summary>Przedziały miesięczne (miesiące kalendarzowe przycięte do zakresu).</summary>
    Month,
}

/// <summary>Zakres dat [<see cref="From"/>, <see cref="To"/>] włącznie.</summary>
public sealed record DateRange(DateOnly From, DateOnly To)
{
    /// <summary>Liczba dni zakresu (z oboma końcami).</summary>
    public int Days => To.DayNumber - From.DayNumber + 1;

    public bool Contains(DateOnly date) => date >= From && date <= To;
}

/// <summary>
/// Rozwiązany okres raportu (SF-19, daty w Europe/Warsaw): zakres, efektywny początek (przełom
/// uruchomienia), okres bazowy porównania i agregacja wykresu. Bez I/O.
/// </summary>
/// <param name="Kind">Wybór okresu.</param>
/// <param name="Range">Zakres wybranego okresu [from, to].</param>
/// <param name="EffectiveFrom">Pierwszy liczony dzień: max(from, data uruchomienia).</param>
/// <param name="LaunchDate">Data uruchomienia.</param>
/// <param name="InProgress">Bieżący miesiąc albo bieżący rok (opis porównania).</param>
/// <param name="Granularity">Dni albo miesiące wykresu.</param>
/// <param name="Base">Okres bazowy porównania.</param>
public sealed record ReportPeriod(
    PeriodKind Kind,
    DateRange Range,
    DateOnly EffectiveFrom,
    DateOnly LaunchDate,
    bool InProgress,
    Granularity Granularity,
    DateRange Base)
{
    /// <summary>Własny zakres o efektywnej długości do tylu dni agreguje się po dniach, dłuższy po miesiącach.</summary>
    public const int CustomDailyMaxDays = 62;

    /// <summary>Okres w całości przed uruchomieniem: brak danych (nie zera), źródła nie są odpytywane.</summary>
    public bool BeforeLaunch => EffectiveFrom > Range.To;

    /// <summary>Porównanie jest możliwe tylko, gdy okres bazowy nie zaczyna się przed datą uruchomienia.</summary>
    public bool BaseAvailable => Base.From >= LaunchDate;

    /// <summary>Liczony zakres [efektywny początek, to]; nie używać przy <see cref="BeforeLaunch"/>.</summary>
    public DateRange Effective => new(EffectiveFrom, Range.To);

    /// <summary>
    /// Najpóźniejszy dopuszczalny koniec własnego zakresu: dziś + 1 rok (29.02 → 28.02). Ogranicza
    /// liczbę przedziałów wykresu; wpisy z datą w przyszłości mieszczą się z zapasem.
    /// </summary>
    public static DateOnly LatestEnd(DateOnly today) => today.AddYears(1);

    /// <summary>
    /// Rozwiązuje wybór okresu. Walidację parametrów (rok ≤ bieżący, wymagane i uporządkowane daty,
    /// koniec ≤ <see cref="LatestEnd"/>) wykonuje wcześniej aplikacja; naruszenie to błąd programu.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Parametry spoza dopuszczalnego zakresu.</exception>
    public static ReportPeriod Resolve(PeriodKind kind, int? year, DateOnly? from, DateOnly? to, DateOnly today, DateOnly launchDate)
    {
        switch (kind)
        {
            case PeriodKind.CurrentMonth:
                {
                    var range = MonthOf(today);
                    return Create(kind, range, launchDate, inProgress: true, Granularity.Day, MonthOf(range.From.AddMonths(-1)));
                }

            case PeriodKind.PreviousMonth:
                {
                    var range = MonthOf(MonthOf(today).From.AddMonths(-1));
                    return Create(kind, range, launchDate, inProgress: false, Granularity.Day, MonthOf(range.From.AddMonths(-1)));
                }

            case PeriodKind.Year:
                {
                    var selected = year ?? today.Year;
                    if (selected > today.Year || selected <= DateOnly.MinValue.Year)
                    {
                        throw new ArgumentOutOfRangeException(nameof(year), selected, "Rok spoza dopuszczalnego zakresu.");
                    }

                    // Jedyne miejsce tej reguły (SF-19, do potwierdzenia przez właściciela): bieżący rok (do
                    // dziś) porównuje się z tym samym odcinkiem poprzedniego roku, zakończony — z całym
                    // poprzednim rokiem.
                    var start = new DateOnly(selected, 1, 1);
                    var previousStart = new DateOnly(selected - 1, 1, 1);
                    return selected == today.Year
                        ? Create(kind, new DateRange(start, today), launchDate, inProgress: true, Granularity.Month, new DateRange(previousStart, today.AddYears(-1)))
                        : Create(kind, new DateRange(start, new DateOnly(selected, 12, 31)), launchDate, inProgress: false, Granularity.Month, new DateRange(previousStart, new DateOnly(selected - 1, 12, 31)));
                }

            case PeriodKind.Custom:
                {
                    if (from is not { } start || to is not { } end || start > end || end > LatestEnd(today))
                    {
                        throw new ArgumentOutOfRangeException(nameof(to), "Własny zakres wymaga dat od ≤ do ≤ dziś + 1 rok.");
                    }

                    var range = new DateRange(start, end);
                    var effectiveFrom = LaunchCutoff.EffectiveFrom(start, launchDate);

                    // Baza: tyle samo dni co efektywny zakres, tuż przed efektywnym początkiem. Przy okresie w
                    // całości przed uruchomieniem — tyle dni co zakres przed jego początkiem (i tak niedostępna).
                    var beforeLaunch = effectiveFrom > end;
                    var anchor = beforeLaunch ? start : effectiveFrom;
                    var days = end.DayNumber - anchor.DayNumber + 1;
                    var granularity = days <= CustomDailyMaxDays ? Granularity.Day : Granularity.Month;
                    return Create(kind, range, launchDate, inProgress: false, granularity, new DateRange(anchor.AddDays(-days), anchor.AddDays(-1)));
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Nieznany okres.");
        }
    }

    /// <summary>
    /// Przedziały wykresu pokrywające [efektywny początek, to] bez luk: dni albo miesiące
    /// kalendarzowe przycięte do zakresu (pierwszy i ostatni mogą być niepełne). Pusta lista przy
    /// <see cref="BeforeLaunch"/>.
    /// </summary>
    public IReadOnlyList<DateRange> Buckets()
    {
        if (BeforeLaunch)
        {
            return [];
        }

        var buckets = new List<DateRange>();
        var start = EffectiveFrom;
        while (start <= Range.To)
        {
            var end = Granularity == Granularity.Day ? start : MonthOf(start).To;
            if (end > Range.To)
            {
                end = Range.To;
            }

            buckets.Add(new DateRange(start, end));
            if (end == DateOnly.MaxValue)
            {
                break;
            }

            start = end.AddDays(1);
        }

        return buckets;
    }

    /// <summary>Indeks przedziału z <see cref="Buckets"/> dla daty z liczonego zakresu.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Data poza liczonym zakresem.</exception>
    public int BucketIndexOf(DateOnly date)
    {
        if (BeforeLaunch || !Effective.Contains(date))
        {
            throw new ArgumentOutOfRangeException(nameof(date), date, "Data poza liczonym zakresem okresu.");
        }

        return Granularity == Granularity.Day
            ? date.DayNumber - EffectiveFrom.DayNumber
            : MonthNumber(date) - MonthNumber(EffectiveFrom);
    }

    /// <summary>
    /// Okres bazowy do liczenia tymi samymi regułami: zakres i efektywny początek =
    /// <see cref="Base"/>. Używać tylko przy <see cref="BaseAvailable"/>.
    /// </summary>
    public ReportPeriod ForBase() => this with { Range = Base, EffectiveFrom = Base.From, InProgress = false };

    private static ReportPeriod Create(PeriodKind kind, DateRange range, DateOnly launchDate, bool inProgress, Granularity granularity, DateRange @base) =>
        new(kind, range, LaunchCutoff.EffectiveFrom(range.From, launchDate), launchDate, inProgress, granularity, @base);

    private static DateRange MonthOf(DateOnly date)
    {
        var first = new DateOnly(date.Year, date.Month, 1);
        return new DateRange(first, first.AddDays(DateTime.DaysInMonth(date.Year, date.Month) - 1));
    }

    private static int MonthNumber(DateOnly date) => (date.Year * 12) + date.Month;
}
