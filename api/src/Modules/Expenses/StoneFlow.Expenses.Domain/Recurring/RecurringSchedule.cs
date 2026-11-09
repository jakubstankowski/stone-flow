namespace StoneFlow.Expenses.Domain.Recurring;

/// <summary>Wynik sprawdzenia zmiany harmonogramu: nowy stan albo naruszenie reguły.</summary>
public sealed record RecurringScheduleEdit(RecurringScheduleState? Next, RecurringRuleViolation? Violation);

/// <summary>Skutek zapisu na dzień: terminy okresów należnych (powstaną od razu) i następny termin po ich utworzeniu.</summary>
public sealed record RecurringOutlook(IReadOnlyList<DateOnly> ImmediateDueDates, DateOnly? NextDueDate);

/// <summary>
/// Czyste funkcje modelu terminów (spec SF-16, reguły 2–7). Wynik zależy wyłącznie od stanu
/// definicji (<see cref="RecurringScheduleState"/>) i rejestru okresów (okres → termin zapisany przy
/// utworzeniu wystąpienia), a nie od kolejności zapisów.
/// </summary>
public static class RecurringSchedule
{
    /// <summary>Twardy limit przeglądania okresów bez końca (zakres dat 2000–2099 to najwyżej 1200 okresów).</summary>
    public const int OpenEndedPeriodLimit = 1200;

    public static readonly IReadOnlyDictionary<RecurringPeriod, DateOnly> EmptyRegistry = new Dictionary<RecurringPeriod, DateOnly>();

    /// <summary>
    /// Reguła 2: dzień z wpisu o największym <c>FromPeriod ≤ p</c>; okres wcześniejszy niż pierwszy
    /// wpis dostaje dzień pierwszego wpisu.
    /// </summary>
    public static int DayOf(RecurringScheduleState state, RecurringPeriod period)
    {
        ArgumentNullException.ThrowIfNull(state);
        var day = state.Days[0].DayOfMonth;
        foreach (var entry in state.Days)
        {
            if (entry.FromPeriod > period)
            {
                break;
            }

            day = entry.DayOfMonth;
        }

        return day;
    }

    /// <summary>Reguła 3: termin okresu; dzień 29–31 w krótszym miesiącu to ostatni dzień miesiąca.</summary>
    public static DateOnly DueDate(RecurringScheduleState state, RecurringPeriod period) => period.DateOn(DayOf(state, period));

    /// <summary>Termin należy do któregoś przedziału wyłączeń <c>[FromDate, ToDate)</c> (suma przedziałów).</summary>
    public static bool IsExcluded(RecurringScheduleState state, DateOnly dueDate)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Exclusions.Any(range => range.Contains(dueDate));
    }

    /// <summary>
    /// Reguła 4: okres się kwalifikuje, gdy definicja nie jest wstrzymana, termin leży w
    /// [początek, koniec] i poza wyłączeniami, a okresu nie ma w rejestrze.
    /// </summary>
    public static bool Qualifies(RecurringScheduleState state, IReadOnlyDictionary<RecurringPeriod, DateOnly> registry, RecurringPeriod period)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(registry);
        if (state.IsPaused || registry.ContainsKey(period))
        {
            return false;
        }

        var due = DueDate(state, period);
        return InRange(state, due) && !IsExcluded(state, due);
    }

    /// <summary>Okresy należne na dzień <paramref name="day"/> (kwalifikujące się, z terminem ≤ dnia), rosnąco.</summary>
    public static IReadOnlyList<RecurringPeriod> DuePeriods(
        RecurringScheduleState state, IReadOnlyDictionary<RecurringPeriod, DateOnly> registry, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.IsPaused || day < state.StartsOn)
        {
            return [];
        }

        var last = RecurringPeriod.Of(state.EndsOn is { } endsOn && endsOn < day ? endsOn : day);
        var due = new List<RecurringPeriod>();
        for (var period = RecurringPeriod.Of(state.StartsOn); period <= last; period = period.Next())
        {
            if (DueDate(state, period) <= day && Qualifies(state, registry, period))
            {
                due.Add(period);
            }
        }

        return due;
    }

    /// <summary>
    /// Reguła 6, „najbliższy nieutworzony termin”: termin najwcześniejszego kwalifikującego się okresu
    /// (może być ≤ dzisiaj); <c>null</c>, gdy nie kwalifikuje się żaden okres (zawsze przy wstrzymaniu).
    /// </summary>
    public static DateOnly? NextUncreatedDueDate(RecurringScheduleState state, IReadOnlyDictionary<RecurringPeriod, DateOnly> registry) =>
        FirstQualifyingAfter(state, registry, after: null);

    /// <summary>Reguła 6, „następny termin po zapisie”: najwcześniejszy kwalifikujący się termin późniejszy niż <paramref name="day"/>.</summary>
    public static DateOnly? NextDueDateAfter(RecurringScheduleState state, IReadOnlyDictionary<RecurringPeriod, DateOnly> registry, DateOnly day) =>
        FirstQualifyingAfter(state, registry, day);

    /// <summary>Podgląd zapisu (dodanie, zmiana, wznowienie): co powstanie od razu i kiedy następne wystąpienie.</summary>
    public static RecurringOutlook Outlook(RecurringScheduleState state, IReadOnlyDictionary<RecurringPeriod, DateOnly> registry, DateOnly day) =>
        new([.. DuePeriods(state, registry, day).Select(period => DueDate(state, period))], NextDueDateAfter(state, registry, day));

    /// <summary>Termin efektywny: zapisany w rejestrze dla okresu z wystąpieniem, w pozostałych przypadkach termin okresu.</summary>
    public static DateOnly EffectiveDueDate(
        RecurringScheduleState state, IReadOnlyDictionary<RecurringPeriod, DateOnly> registry, RecurringPeriod period)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return registry.TryGetValue(period, out var registered) ? registered : DueDate(state, period);
    }

    /// <summary>
    /// Reguła 6, „ostatni termin”: najpóźniejszy termin efektywny w [początek, koniec]; bez końca, albo
    /// gdy w zakresie nie ma terminu, <c>null</c>. Nie zależy od wyłączeń ani wstrzymania.
    /// </summary>
    public static DateOnly? LastDueDate(RecurringScheduleState state, IReadOnlyDictionary<RecurringPeriod, DateOnly> registry)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.EndsOn is not { } endsOn)
        {
            return null;
        }

        DateOnly? last = null;
        for (var period = RecurringPeriod.Of(state.StartsOn); period <= RecurringPeriod.Of(endsOn); period = period.Next())
        {
            var due = EffectiveDueDate(state, registry, period);
            if (InRange(state, due))
            {
                last = due;
            }
        }

        return last;
    }

    /// <summary>Co najmniej jeden termin efektywny w [początek, koniec] (bez końca zawsze prawda).</summary>
    public static bool HasEffectiveDueInRange(RecurringScheduleState state, IReadOnlyDictionary<RecurringPeriod, DateOnly> registry)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.EndsOn is null || LastDueDate(state, registry) is not null;
    }

    /// <summary>Reguła 7: „Zakończony” (ostatni termin ≤ dzisiaj), potem „Wstrzymany”, w pozostałych przypadkach „Aktywny”.</summary>
    public static RecurringDefinitionStatus Status(DateOnly? lastDueDate, DateOnly? pausedOn, DateOnly today) =>
        lastDueDate is { } last && last <= today ? RecurringDefinitionStatus.Ended
        : pausedOn is not null ? RecurringDefinitionStatus.Paused
        : RecurringDefinitionStatus.Active;

    /// <summary>
    /// Zmiana harmonogramu w kolejności NC3: najpierw zakaz zmiany dnia na definicji wstrzymanej,
    /// potem stan po zmianie (reguły 2 i 5) i co najmniej jeden termin efektywny w zakresie.
    /// </summary>
    public static RecurringScheduleEdit Edit(
        RecurringScheduleState state, RecurringScheduleInput schedule, DateOnly today, IReadOnlyDictionary<RecurringPeriod, DateOnly> registry)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(schedule);
        if (state.IsPaused && schedule.DayOfMonth != state.DayOfMonth)
        {
            return new RecurringScheduleEdit(null, RecurringRuleViolation.PausedDayChange);
        }

        var next = state.WithSchedule(schedule, today);
        return HasEffectiveDueInRange(next, registry)
            ? new RecurringScheduleEdit(next, null)
            : new RecurringScheduleEdit(null, RecurringRuleViolation.NoDueInRange);
    }

    private static bool InRange(RecurringScheduleState state, DateOnly due) =>
        due >= state.StartsOn && (state.EndsOn is not { } endsOn || due <= endsOn);

    private static DateOnly? FirstQualifyingAfter(
        RecurringScheduleState state, IReadOnlyDictionary<RecurringPeriod, DateOnly> registry, DateOnly? after)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.IsPaused)
        {
            return null;
        }

        var first = RecurringPeriod.Of(state.StartsOn);
        if (after is { } day && RecurringPeriod.Of(day) > first)
        {
            first = RecurringPeriod.Of(day);
        }

        var last = state.EndsOn is { } endsOn
            ? RecurringPeriod.Of(endsOn)
            : RecurringPeriod.Of(state.StartsOn).AddMonths(OpenEndedPeriodLimit);
        for (var period = first; period <= last; period = period.Next())
        {
            if (Qualifies(state, registry, period) && (after is not { } limit || DueDate(state, period) > limit))
            {
                return DueDate(state, period);
            }
        }

        return null;
    }
}
