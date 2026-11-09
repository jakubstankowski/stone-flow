namespace StoneFlow.Expenses.Domain.Recurring;

/// <summary>
/// Okres wydatku cyklicznego: miesiąc kalendarzowy (rok, miesiąc). W bazie pierwszy dzień miesiąca
/// (<see cref="FirstDay"/>), w API <c>yyyy-MM</c>.
/// </summary>
public readonly record struct RecurringPeriod : IComparable<RecurringPeriod>
{
    public RecurringPeriod(int year, int month)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, 9999);
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        Year = year;
        Month = month;
    }

    public int Year { get; }

    public int Month { get; }

    public DateOnly FirstDay => new(Year, Month, 1);

    public int DaysInMonth => DateTime.DaysInMonth(Year, Month);

    private int Index => (Year * 12) + Month - 1;

    public static RecurringPeriod Of(DateOnly date) => new(date.Year, date.Month);

    public static bool operator <(RecurringPeriod left, RecurringPeriod right) => left.CompareTo(right) < 0;

    public static bool operator <=(RecurringPeriod left, RecurringPeriod right) => left.CompareTo(right) <= 0;

    public static bool operator >(RecurringPeriod left, RecurringPeriod right) => left.CompareTo(right) > 0;

    public static bool operator >=(RecurringPeriod left, RecurringPeriod right) => left.CompareTo(right) >= 0;

    public RecurringPeriod Next() => AddMonths(1);

    public RecurringPeriod Previous() => AddMonths(-1);

    public RecurringPeriod AddMonths(int months)
    {
        var index = Index + months;
        return new RecurringPeriod(index / 12, (index % 12) + 1);
    }

    /// <summary>Data w okresie dla dnia miesiąca; dzień większy niż liczba dni okresu to ostatni dzień (31 → 28.02).</summary>
    public DateOnly DateOn(int dayOfMonth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dayOfMonth, 1);
        return new DateOnly(Year, Month, Math.Min(dayOfMonth, DaysInMonth));
    }

    public int CompareTo(RecurringPeriod other) => Index.CompareTo(other.Index);
}
