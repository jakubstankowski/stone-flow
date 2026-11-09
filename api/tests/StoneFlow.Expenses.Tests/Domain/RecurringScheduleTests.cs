using StoneFlow.Expenses.Domain.Recurring;
using static StoneFlow.Expenses.Tests.Domain.RecurringSim;

namespace StoneFlow.Expenses.Tests.Domain;

/// <summary>Czyste funkcje modelu terminów (reguły 1–7 spec SF-16).</summary>
public sealed class RecurringScheduleTests
{
    private static readonly IReadOnlyDictionary<RecurringPeriod, DateOnly> NoRegistry = RecurringSchedule.EmptyRegistry;

    public static TheoryData<int, int, int> DaysAndMonths()
    {
        var data = new TheoryData<int, int, int>();
        foreach (var day in new[] { 1, 28, 29, 30, 31 })
        {
            for (var month = 1; month <= 12; month++)
            {
                data.Add(day, month, 2027);
            }

            data.Add(day, 2, 2028);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DaysAndMonths))]
    public void Due_date_is_the_day_or_the_last_day_of_a_shorter_month(int day, int month, int year)
    {
        var state = State(day, D(1, 1, 2027));

        var due = RecurringSchedule.DueDate(state, new RecurringPeriod(year, month));

        Assert.Equal(new DateOnly(year, month, Math.Min(day, DateTime.DaysInMonth(year, month))), due);
    }

    [Fact]
    public void Day_31_falls_on_the_last_day_of_february_and_april()
    {
        var state = State(31, D(1, 1, 2027));

        Assert.Equal(D(31, 1, 2027), RecurringSchedule.DueDate(state, new RecurringPeriod(2027, 1)));
        Assert.Equal(D(28, 2, 2027), RecurringSchedule.DueDate(state, new RecurringPeriod(2027, 2)));
        Assert.Equal(D(29, 2, 2028), RecurringSchedule.DueDate(state, new RecurringPeriod(2028, 2)));
        Assert.Equal(D(31, 3, 2027), RecurringSchedule.DueDate(state, new RecurringPeriod(2027, 3)));
        Assert.Equal(D(30, 4, 2027), RecurringSchedule.DueDate(state, new RecurringPeriod(2027, 4)));
    }

    [Fact]
    public void December_is_followed_by_january_of_the_next_year()
    {
        Assert.Equal(new RecurringPeriod(2027, 1), new RecurringPeriod(2026, 12).Next());
        Assert.Equal(new RecurringPeriod(2026, 12), new RecurringPeriod(2027, 1).Previous());

        var state = State(10, D(1, 12));
        Assert.Equal([D(10, 12), D(10, 1, 2027)], Dues(state, D(15, 1, 2027)));
    }

    [Fact]
    public void Day_of_a_period_comes_from_the_latest_entry_and_periods_before_the_first_entry_get_the_first_day()
    {
        var state = new RecurringScheduleState(
            D(1, 9),
            null,
            null,
            [new RecurringDayEntry(new RecurringPeriod(2026, 11), 20), new RecurringDayEntry(new RecurringPeriod(2027, 3), 10)],
            []);

        Assert.Equal(20, RecurringSchedule.DayOf(state, new RecurringPeriod(2026, 9)));
        Assert.Equal(20, RecurringSchedule.DayOf(state, new RecurringPeriod(2026, 11)));
        Assert.Equal(20, RecurringSchedule.DayOf(state, new RecurringPeriod(2027, 2)));
        Assert.Equal(10, RecurringSchedule.DayOf(state, new RecurringPeriod(2027, 3)));
        Assert.Equal(10, state.DayOfMonth);
    }

    [Theory]
    [InlineData(15, 3, false)]
    [InlineData(10, 3, true)]
    public void Start_is_inclusive(int startDay, int startMonth, bool marchCounts)
    {
        var state = State(10, D(startDay, startMonth));

        Assert.Equal(marchCounts, RecurringSchedule.Qualifies(state, NoRegistry, new RecurringPeriod(2026, 3)));
        Assert.Equal(marchCounts ? D(10, 3) : D(10, 4), RecurringSchedule.NextUncreatedDueDate(state, NoRegistry));
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(9, false)]
    public void End_is_inclusive(int endDay, bool juneCounts)
    {
        var state = State(10, D(1, 1), D(endDay, 6));

        Assert.Equal(juneCounts, RecurringSchedule.Qualifies(state, NoRegistry, new RecurringPeriod(2026, 6)));
        Assert.Equal(juneCounts ? D(10, 6) : D(10, 5), RecurringSchedule.LastDueDate(state, NoRegistry));
    }

    [Fact]
    public void Start_31_january_with_day_31_and_end_28_february_gives_two_occurrences()
    {
        var state = State(31, D(31, 1, 2027), D(28, 2, 2027));

        Assert.Equal([D(31, 1, 2027), D(28, 2, 2027)], Dues(state, D(1, 6, 2027)));
    }

    [Fact]
    public void Range_without_any_due_date_has_no_effective_due()
    {
        var schedule = new RecurringScheduleInput(10, D(15, 3), D(20, 3));

        Assert.False(RecurringSchedule.HasEffectiveDueInRange(RecurringScheduleState.New(schedule), NoRegistry));
        Assert.Equal(RecurringRuleViolation.NoDueInRange, RecurringExpenseDefinition.CheckNew(schedule));
        Assert.Throws<ArgumentException>(() => RecurringExpenseDefinition.Create(Guid.NewGuid(), Content(), schedule, Now));
    }

    [Fact]
    public void Exclusion_excludes_its_from_day_but_not_its_to_day()
    {
        var excludedFrom = WithExclusion(State(10, D(1, 1)), D(10, 3), D(10, 4));

        Assert.False(RecurringSchedule.Qualifies(excludedFrom, NoRegistry, new RecurringPeriod(2026, 3)));
        Assert.True(RecurringSchedule.Qualifies(excludedFrom, NoRegistry, new RecurringPeriod(2026, 4)));
    }

    [Fact]
    public void Paused_definition_has_no_qualifying_period()
    {
        var state = State(10, D(1, 1)).Paused(D(5, 11));

        Assert.False(RecurringSchedule.Qualifies(state, NoRegistry, new RecurringPeriod(2026, 12)));
        Assert.Empty(RecurringSchedule.DuePeriods(state, NoRegistry, D(20, 11)));
        Assert.Null(RecurringSchedule.NextUncreatedDueDate(state, NoRegistry));
        Assert.Null(RecurringSchedule.NextDueDateAfter(state, NoRegistry, D(20, 11)));
    }

    [Fact]
    public void Registered_period_never_qualifies_again_even_when_its_expense_was_deleted()
    {
        var state = State(10, D(1, 1));
        var registry = new Dictionary<RecurringPeriod, DateOnly> { [new RecurringPeriod(2026, 1)] = D(10, 1) };

        Assert.False(RecurringSchedule.Qualifies(state, registry, new RecurringPeriod(2026, 1)));
        Assert.Equal(D(10, 2), RecurringSchedule.NextUncreatedDueDate(state, registry));
    }

    [Fact]
    public void Due_periods_include_today_but_not_tomorrow()
    {
        var state = State(20, D(1, 11));

        Assert.Equal([D(20, 11)], Dues(state, D(20, 11)));
        Assert.Empty(Dues(state, D(19, 11)));
    }

    [Fact]
    public void Next_uncreated_due_date_can_be_due_and_after_the_last_due_date_ends_the_definition()
    {
        var state = State(10, D(1, 1), D(30, 11));
        var registry = Enumerable.Range(1, 10).ToDictionary(month => new RecurringPeriod(2026, month), month => D(10, month));

        Assert.Equal(D(10, 11), RecurringSchedule.NextUncreatedDueDate(state, registry));
        Assert.Equal(D(10, 11), RecurringSchedule.LastDueDate(state, registry));
        Assert.Equal(RecurringDefinitionStatus.Ended, RecurringSchedule.Status(D(10, 11), null, D(20, 11)));
        Assert.Null(RecurringSchedule.NextDueDateAfter(state, registry, D(20, 11)));
    }

    [Fact]
    public void Last_due_date_uses_the_registered_due_date_of_a_created_period()
    {
        // 05.11–25.11, dzień 10 → 28; listopad utworzony 10.11: zapis przechodzi, a stan to „Zakończony”.
        var state = State(28, D(5, 11), D(25, 11));
        var registry = new Dictionary<RecurringPeriod, DateOnly> { [new RecurringPeriod(2026, 11)] = D(10, 11) };

        Assert.False(RecurringSchedule.HasEffectiveDueInRange(state, NoRegistry));
        Assert.True(RecurringSchedule.HasEffectiveDueInRange(state, registry));
        Assert.Equal(D(10, 11), RecurringSchedule.LastDueDate(state, registry));
        Assert.Equal(RecurringDefinitionStatus.Ended, RecurringSchedule.Status(RecurringSchedule.LastDueDate(state, registry), null, D(20, 11)));
    }

    [Fact]
    public void No_further_due_date_before_the_end_ends_the_definition()
    {
        var state = State(28, D(1, 1), D(25, 11));

        Assert.Equal(D(28, 10), RecurringSchedule.LastDueDate(state, NoRegistry));
        Assert.Equal(RecurringDefinitionStatus.Ended, RecurringSchedule.Status(D(28, 10), null, D(20, 11)));
    }

    [Theory]
    [InlineData(null, null, RecurringDefinitionStatus.Active)]
    [InlineData(null, "2026-11-05", RecurringDefinitionStatus.Paused)]
    [InlineData("2026-12-10", "2026-11-05", RecurringDefinitionStatus.Paused)]
    [InlineData("2026-11-20", "2026-11-05", RecurringDefinitionStatus.Ended)]
    [InlineData("2026-11-20", null, RecurringDefinitionStatus.Ended)]
    [InlineData("2026-11-21", null, RecurringDefinitionStatus.Active)]
    public void Ended_takes_precedence_over_paused(string? lastDue, string? pausedOn, RecurringDefinitionStatus expected)
    {
        DateOnly? Parse(string? text) => text is null ? null : DateOnly.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, RecurringSchedule.Status(Parse(lastDue), Parse(pausedOn), D(20, 11)));
    }

    [Fact]
    public void Last_due_date_comes_from_the_day_history()
    {
        // Koniec 25.06, dzień 28 do października, od listopada 20: czerwiec (28.06) jest po końcu.
        var state = new RecurringScheduleState(
            D(1, 1),
            D(25, 6),
            null,
            [new RecurringDayEntry(new RecurringPeriod(2026, 1), 28), new RecurringDayEntry(new RecurringPeriod(2026, 11), 20)],
            []);

        Assert.Equal(D(28, 5), RecurringSchedule.LastDueDate(state, NoRegistry));
    }

    private static RecurringScheduleState State(int day, DateOnly startsOn, DateOnly? endsOn = null) =>
        RecurringScheduleState.New(new RecurringScheduleInput(day, startsOn, endsOn));

    private static RecurringScheduleState WithExclusion(RecurringScheduleState state, DateOnly from, DateOnly to) =>
        new(state.StartsOn, state.EndsOn, state.PausedOn, state.Days, [.. state.Exclusions, new RecurringExclusionRange(from, to, RecurringExclusionReason.Pause)]);

    private static DateOnly[] Dues(RecurringScheduleState state, DateOnly day) =>
        [.. RecurringSchedule.DuePeriods(state, NoRegistry, day).Select(period => RecurringSchedule.DueDate(state, period))];
}
