namespace StoneFlow.Expenses.Domain.Recurring;

/// <summary>Wpis historii dni: dzień miesiąca obowiązujący od okresu <paramref name="FromPeriod"/>.</summary>
public sealed record RecurringDayEntry(RecurringPeriod FromPeriod, int DayOfMonth);

/// <summary>Przedział wyłączenia terminów <c>[FromDate, ToDate)</c>.</summary>
public sealed record RecurringExclusionRange(DateOnly FromDate, DateOnly ToDate, RecurringExclusionReason Reason)
{
    public bool Contains(DateOnly date) => date >= FromDate && date < ToDate;
}

/// <summary>
/// Stan harmonogramu definicji, z którego czyste funkcje <see cref="RecurringSchedule"/> liczą okresy
/// (reguły 2–7): początek, koniec, dzień wstrzymania, historia dni (rosnąco, co najmniej jeden wpis)
/// i przedziały wyłączeń. Operacje zwracają nowy stan (podgląd liczy na kopii, zapis przenosi wynik do
/// encji).
/// </summary>
public sealed class RecurringScheduleState
{
    public RecurringScheduleState(
        DateOnly startsOn,
        DateOnly? endsOn,
        DateOnly? pausedOn,
        IEnumerable<RecurringDayEntry> days,
        IEnumerable<RecurringExclusionRange> exclusions)
    {
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(exclusions);
        StartsOn = startsOn;
        EndsOn = endsOn;
        PausedOn = pausedOn;
        Days = [.. days.OrderBy(day => day.FromPeriod)];
        Exclusions = [.. exclusions];
        if (Days.Count == 0)
        {
            throw new ArgumentException("Historia dni definicji musi mieć co najmniej jeden wpis.", nameof(days));
        }
    }

    public DateOnly StartsOn { get; }

    public DateOnly? EndsOn { get; }

    public DateOnly? PausedOn { get; }

    /// <summary>Historia dni posortowana rosnąco po okresie.</summary>
    public IReadOnlyList<RecurringDayEntry> Days { get; }

    public IReadOnlyList<RecurringExclusionRange> Exclusions { get; }

    /// <summary>Bieżący dzień: dzień ostatniego wpisu historii (dla bieżącego i przyszłych okresów).</summary>
    public int DayOfMonth => Days[^1].DayOfMonth;

    public bool IsPaused => PausedOn is not null;

    /// <summary>Nowa definicja: jeden wpis dnia od okresu początku, bez wyłączeń, niewstrzymana.</summary>
    public static RecurringScheduleState New(RecurringScheduleInput schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        return new RecurringScheduleState(
            schedule.StartsOn,
            schedule.EndsOn,
            pausedOn: null,
            [new RecurringDayEntry(RecurringPeriod.Of(schedule.StartsOn), schedule.DayOfMonth)],
            []);
    }

    /// <summary>
    /// Zmiana harmonogramu (reguły 2 i 5), bez sprawdzania reguł: nowy dzień obowiązuje od bieżącego
    /// okresu (wpisy od bieżącego i późniejszych okresów są zastępowane jednym wpisem), a przesunięcie
    /// początku na wcześniej dodaje przedział <see cref="RecurringExclusionReason.StartMoved"/>
    /// <c>[nowy początek, min(stary początek, dzisiaj))</c>, o ile jest niepusty.
    /// </summary>
    public RecurringScheduleState WithSchedule(RecurringScheduleInput schedule, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        var days = Days.ToList();
        if (schedule.DayOfMonth != DayOfMonth)
        {
            var current = RecurringPeriod.Of(today);
            days.RemoveAll(day => day.FromPeriod >= current);
            days.Add(new RecurringDayEntry(current, schedule.DayOfMonth));
        }

        var exclusions = Exclusions.ToList();
        if (schedule.StartsOn < StartsOn)
        {
            var to = StartsOn < today ? StartsOn : today;
            if (schedule.StartsOn < to)
            {
                exclusions.Add(new RecurringExclusionRange(schedule.StartsOn, to, RecurringExclusionReason.StartMoved));
            }
        }

        return new RecurringScheduleState(schedule.StartsOn, schedule.EndsOn, PausedOn, days, exclusions);
    }

    /// <summary>Wstrzymanie: tylko <c>PausedOn = dzisiaj</c> (przedział powstaje przy wznowieniu).</summary>
    public RecurringScheduleState Paused(DateOnly today) => new(StartsOn, EndsOn, today, Days, Exclusions);

    /// <summary>
    /// Wznowienie: przedział <c>[PausedOn, dzisiaj)</c> z powodem <see cref="RecurringExclusionReason.Pause"/>
    /// (pusty przy tym samym dniu, więc niezapisywany) i wyczyszczenie <c>PausedOn</c>. Termin w dniu
    /// wznowienia się liczy.
    /// </summary>
    public RecurringScheduleState Resumed(DateOnly today)
    {
        var exclusions = Exclusions.ToList();
        if (PausedOn is { } pausedOn && pausedOn < today)
        {
            exclusions.Add(new RecurringExclusionRange(pausedOn, today, RecurringExclusionReason.Pause));
        }

        return new RecurringScheduleState(StartsOn, EndsOn, pausedOn: null, Days, exclusions);
    }
}
