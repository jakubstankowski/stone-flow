using StoneFlow.Expenses.Domain.Expenses;
using StoneFlow.Expenses.Domain.Recurring;
using static StoneFlow.Expenses.Tests.Domain.RecurringSim;

namespace StoneFlow.Expenses.Tests.Domain;

/// <summary>Operacje definicji i skutki modelu terminów (reguła 8, Edge Cases, NC1–NC6 spec SF-16).</summary>
public sealed class RecurringExpenseDefinitionTests
{
    [Fact]
    public void Create_adds_one_day_entry_from_the_start_period_and_occurrences_up_to_today_inclusive()
    {
        var sim = Create(5, D(1, 9), null, D(5, 11));

        var entry = Assert.Single(sim.Definition.Days);
        Assert.Equal(D(1, 9), entry.FromPeriod);
        Assert.Equal(5, entry.DayOfMonth);
        Assert.Empty(sim.Definition.Exclusions);
        Assert.Null(sim.Definition.LastDueDate);
        Assert.Equal([D(5, 9), D(5, 10), D(5, 11)], sim.Created);
    }

    [Fact]
    public void Occurrence_is_a_planned_cost_with_cost_date_and_due_date_equal_to_the_due_date()
    {
        var sim = Create(31, D(1, 1, 2027), null, D(1, 1, 2027));

        var occurrence = sim.Definition.CreateOccurrence(Guid.NewGuid(), new RecurringPeriod(2027, 2), Now);

        Assert.Null(occurrence.Expense.PaidOn);
        Assert.False(occurrence.Expense.IsPaid);
        Assert.Equal(D(28, 2, 2027), occurrence.Expense.CostDate);
        Assert.Equal(D(28, 2, 2027), occurrence.Expense.DueDate);
        Assert.Null(occurrence.Expense.DocumentReference);
        Assert.Null(occurrence.Expense.CreationRequestId);
        Assert.Equal(sim.Definition.Id, occurrence.Expense.RecurringDefinitionId);
        Assert.Equal(D(1, 2, 2027), occurrence.Expense.RecurringPeriod);
        Assert.Equal(300m, occurrence.Expense.GrossAmount);
        Assert.Equal(D(1, 2, 2027), occurrence.Period.Period);
        Assert.Equal(occurrence.Expense.Id, occurrence.Period.ExpenseId);
    }

    [Fact]
    public void Answer_1_current_period_without_occurrence_is_created_at_save_when_the_new_due_date_has_passed()
    {
        var sim = Create(25, D(1, 10), null, D(25, 10));

        var (change, created) = sim.Change(D(20, 11), day: 5);

        Assert.True(change.Succeeded);
        Assert.Equal([D(5, 11)], created);
        Assert.Equal(D(5, 12), sim.NextDueDate());
    }

    [Fact]
    public void Answer_1_future_new_due_date_is_created_on_the_due_date()
    {
        var sim = Create(25, D(1, 10), null, D(25, 10));

        var (_, created) = sim.Change(D(20, 11), day: 28);

        Assert.Empty(created);
        Assert.Equal(D(28, 11), sim.NextDueDate());
        Assert.Equal([D(28, 11)], sim.Run(D(28, 11)));
    }

    [Fact]
    public void Answer_1_period_with_an_occurrence_does_not_get_a_second_one()
    {
        var sim = Create(10, D(1, 11), null, D(10, 11));

        var (_, created) = sim.Change(D(20, 11), day: 25);

        Assert.Empty(created);
        Assert.Equal(D(25, 12), sim.NextDueDate());
        Assert.Equal([D(10, 11)], sim.Created);
    }

    [Fact]
    public void Answer_2_extension_creates_the_periods_between_the_old_end_and_today()
    {
        var sim = Create(10, D(1, 1), D(30, 6), D(30, 6));

        var (_, created) = sim.Change(D(20, 11), noEnd: true);

        Assert.Equal([D(10, 7), D(10, 8), D(10, 9), D(10, 10), D(10, 11)], created);
    }

    [Fact]
    public void Answer_2_extension_skips_the_periods_of_a_pause()
    {
        var sim = Create(10, D(1, 1), D(30, 6), D(1, 1));
        sim.Pause(D(1, 5));
        sim.Resume(D(15, 8));

        var (_, created) = sim.Change(D(20, 11), noEnd: true);

        Assert.Equal([D(10, 1), D(10, 2), D(10, 3), D(10, 4), D(10, 9), D(10, 10), D(10, 11)], sim.Created);
        Assert.Equal([D(10, 9), D(10, 10), D(10, 11)], created);
    }

    [Fact]
    public void Answer_3_due_date_inside_the_pause_is_not_created_and_the_first_occurrence_is_next_month()
    {
        var sim = Create(10, D(1, 10), null, D(10, 10));
        sim.Pause(D(1, 11));

        var (change, created) = sim.Resume(D(15, 11));

        Assert.True(change.Succeeded);
        Assert.Empty(created);
        var exclusion = Assert.Single(sim.Definition.Exclusions);
        Assert.Equal((D(1, 11), D(15, 11), RecurringExclusionReason.Pause), (exclusion.FromDate, exclusion.ToDate, exclusion.Reason));
        Assert.Equal(D(10, 12), sim.NextDueDate());
    }

    [Fact]
    public void Answer_3_due_date_on_the_resume_day_is_created()
    {
        var sim = Create(10, D(1, 10), null, D(10, 10));
        sim.Pause(D(1, 11));

        var (_, created) = sim.Resume(D(10, 11));

        Assert.Equal([D(10, 11)], created);
    }

    [Fact]
    public void Pause_and_resume_on_the_same_day_store_no_exclusion()
    {
        var sim = Create(10, D(1, 10), null, D(10, 10));
        sim.Pause(D(5, 11));

        sim.Resume(D(5, 11));

        Assert.Empty(sim.Definition.Exclusions);
        Assert.Equal(D(10, 11), sim.NextDueDate());
    }

    [Fact]
    public void Pause_first_creates_the_due_occurrence_of_today()
    {
        var sim = Create(10, D(1, 10), null, D(10, 10));

        var (change, created) = sim.Pause(D(10, 11));

        Assert.True(change.Succeeded);
        Assert.Equal([D(10, 11)], created);
        Assert.Equal(D(10, 11), sim.Definition.PausedOn);
        var pausedOn = Assert.Single(change.Changes);
        Assert.Equal(new RecurringFieldChange(RecurringDefinitionField.PausedOn, null, D(10, 11)), pausedOn);
    }

    [Fact]
    public void Pause_on_the_due_day_after_creation_and_resume_the_same_day_promise_no_occurrence()
    {
        var sim = Create(10, D(1, 10), null, D(10, 10));
        sim.Run(D(10, 11));
        sim.Pause(D(10, 11));

        var outlook = RecurringSchedule.Outlook(sim.Definition.Schedule.Resumed(D(10, 11)), sim.Registry, D(10, 11));

        Assert.Empty(outlook.ImmediateDueDates);
        Assert.Equal(D(10, 12), outlook.NextDueDate);
    }

    [Fact]
    public void Pause_is_rejected_when_already_paused_or_ended_and_resume_when_not_paused()
    {
        var paused = Create(10, D(1, 10), null, D(10, 10));
        paused.Pause(D(1, 11));
        var ended = Create(28, D(1, 1), D(25, 11), D(20, 11));
        var active = Create(10, D(1, 10), null, D(10, 10));

        Assert.Equal(RecurringRuleViolation.AlreadyPaused, paused.Pause(D(2, 11)).Change.Violation);
        Assert.Equal(RecurringRuleViolation.Ended, ended.Pause(D(20, 11)).Change.Violation);
        Assert.Equal(RecurringRuleViolation.NotPaused, active.Resume(D(20, 11)).Change.Violation);
        Assert.Null(ended.Definition.PausedOn);
    }

    [Fact]
    public void Resume_after_the_end_is_allowed_and_creates_nothing()
    {
        var sim = Create(10, D(1, 9), D(15, 11), D(1, 11));
        sim.Pause(D(1, 11));

        var (change, created) = sim.Resume(D(20, 11));

        Assert.True(change.Succeeded);
        Assert.Empty(created);
        Assert.Equal(RecurringDefinitionStatus.Ended, sim.Definition.StatusOn(D(20, 11)));
    }

    [Fact]
    public void V2_march_before_the_start_keeps_its_day_after_a_day_change()
    {
        var sim = Create(10, D(15, 3), null, D(19, 11));

        var (_, created) = sim.Change(D(20, 11), day: 20);

        Assert.Empty(created);
        Assert.DoesNotContain(new RecurringPeriod(2026, 3), sim.Registry.Keys);
        Assert.Equal(10, RecurringSchedule.DayOf(sim.Definition.Schedule, new RecurringPeriod(2026, 3)));
    }

    [Fact]
    public void V2_august_inside_a_pause_stays_excluded_after_a_day_change()
    {
        var sim = Create(10, D(1, 7), null, D(10, 7));
        sim.Pause(D(1, 8));
        sim.Resume(D(15, 8));
        sim.Run(D(9, 11));

        var (_, created) = sim.Change(D(20, 11), day: 20);

        Assert.Equal([D(20, 11)], created);
        Assert.Equal([D(10, 7), D(10, 9), D(10, 10), D(20, 11)], sim.Created);
    }

    [Fact]
    public void V3_critical1_day_change_on_the_creation_day_replaces_the_entry_from_the_start()
    {
        var sim = Create(10, D(20, 11), null, D(20, 11));

        var (_, created) = sim.Change(D(20, 11), day: 25);

        Assert.Empty(created);
        var entry = Assert.Single(sim.Definition.Days);
        Assert.Equal((D(1, 11), 25), (entry.FromPeriod, entry.DayOfMonth));
        Assert.Equal(D(25, 11), sim.NextDueDate());
    }

    [Fact]
    public void V3_critical1_with_a_future_start_removes_the_later_entry()
    {
        var sim = Create(10, D(15, 12), null, D(20, 11));

        sim.Change(D(20, 11), day: 20);

        var entry = Assert.Single(sim.Definition.Days);
        Assert.Equal((D(1, 11), 20), (entry.FromPeriod, entry.DayOfMonth));
        Assert.Equal(D(20, 12), sim.NextDueDate());
    }

    [Fact]
    public void V3_critical2_june_after_the_end_is_not_created_until_the_definition_is_extended()
    {
        var sim = Create(28, D(1, 1), D(25, 6), D(19, 11));

        var (_, afterDay) = sim.Change(D(20, 11), day: 20);

        Assert.Empty(afterDay);
        Assert.Equal(D(28, 5), sim.Definition.LastDueDate);
        Assert.Equal(RecurringDefinitionStatus.Ended, sim.Definition.StatusOn(D(20, 11)));

        var (_, afterExtension) = sim.Change(D(20, 11), endsOn: D(31, 12));

        Assert.Equal([D(28, 6), D(28, 7), D(28, 8), D(28, 9), D(28, 10), D(20, 11)], afterExtension);
    }

    [Fact]
    public void V4_critical_both_orders_and_a_joint_save_give_the_same_periods_and_due_dates()
    {
        var dayFirst = Create(28, D(1, 1), D(25, 6), D(19, 11));
        dayFirst.Change(D(20, 11), day: 20);
        dayFirst.Change(D(21, 11), noEnd: true);

        var extensionFirst = Create(28, D(1, 1), D(25, 6), D(19, 11));
        extensionFirst.Change(D(20, 11), noEnd: true);
        extensionFirst.Change(D(21, 11), day: 20);

        var joint = Create(28, D(1, 1), D(25, 6), D(19, 11));
        joint.Change(D(21, 11), day: 20, noEnd: true);

        DateOnly[] expected = [D(28, 1), D(28, 2), D(28, 3), D(28, 4), D(28, 5), D(28, 6), D(28, 7), D(28, 8), D(28, 9), D(28, 10), D(20, 11)];
        Assert.Equal(expected, dayFirst.Created);
        Assert.Equal(expected, extensionFirst.Created);
        Assert.Equal(expected, joint.Created);
    }

    [Fact]
    public void V4_nc1_intermediate_save_that_created_the_current_period_is_the_only_difference()
    {
        var joint = Create(10, D(1, 1), D(30, 6), D(30, 6));
        joint.Edit(25, D(1, 1), D(15, 11), D(20, 11));

        var extensionFirst = Create(10, D(1, 1), D(30, 6), D(30, 6));
        extensionFirst.Change(D(20, 11), endsOn: D(15, 11));
        extensionFirst.Change(D(20, 11), day: 25);

        var dayFirst = Create(10, D(1, 1), D(30, 6), D(30, 6));
        dayFirst.Change(D(20, 11), day: 25);
        dayFirst.Change(D(20, 11), endsOn: D(15, 11));

        DateOnly[] firstHalf = [D(10, 1), D(10, 2), D(10, 3), D(10, 4), D(10, 5), D(10, 6)];
        DateOnly[] julyToOctober = [D(10, 7), D(10, 8), D(10, 9), D(10, 10)];
        Assert.Equal([.. firstHalf, .. julyToOctober], joint.Created);
        Assert.Equal([.. firstHalf, .. julyToOctober, D(10, 11)], extensionFirst.Created);
        Assert.Equal(joint.Created, dayFirst.Created);
    }

    [Fact]
    public void V4_nc2_due_uncreated_current_period_drops_when_its_new_due_date_falls_before_the_start()
    {
        var sim = Create(10, D(5, 11), null, D(5, 11));
        var before = sim.Definition.DuePeriods(sim.Registry, D(20, 11));

        var (_, created) = sim.Change(D(20, 11), day: 3);

        Assert.Equal([new RecurringPeriod(2026, 11)], before);
        Assert.False(RecurringSchedule.Qualifies(sim.Definition.Schedule, sim.Registry, new RecurringPeriod(2026, 11)));
        Assert.Empty(created);
        Assert.Equal(D(3, 12), sim.NextDueDate());
    }

    [Fact]
    public void Missed_period_keeps_its_due_date_and_the_current_period_gets_the_new_day()
    {
        var sim = Create(10, D(1, 9), null, D(10, 9));

        var (_, created) = sim.Change(D(20, 11), day: 15);

        Assert.Equal([D(10, 10), D(15, 11)], created);
    }

    [Fact]
    public void Current_period_after_a_later_start_is_created_when_the_new_due_date_is_in_range_and_passed()
    {
        var sim = Create(10, D(15, 11), null, D(15, 11));

        var (_, created) = sim.Change(D(20, 11), day: 18);

        Assert.Equal([D(18, 11)], created);
    }

    [Fact]
    public void Future_new_due_date_of_the_current_period_outside_the_range_does_not_count()
    {
        var beforeStart = Create(25, D(5, 11), null, D(20, 11));
        beforeStart.Change(D(20, 11), day: 3);

        var afterEnd = Create(20, D(1, 10), D(25, 11), D(10, 11));
        afterEnd.Change(D(10, 11), day: 28);

        Assert.Equal(D(3, 12), beforeStart.NextDueDate());
        Assert.Equal(D(20, 10), afterEnd.Definition.LastDueDate);
        Assert.Equal(RecurringDefinitionStatus.Ended, afterEnd.Definition.StatusOn(D(10, 11)));
    }

    [Fact]
    public void Day_change_after_resume_in_the_current_period_creates_it_when_outside_the_pause()
    {
        var sim = Create(10, D(1, 10), null, D(10, 10));
        sim.Pause(D(1, 11));
        sim.Resume(D(15, 11));

        var (_, created) = sim.Change(D(20, 11), day: 18);

        Assert.Equal([D(18, 11)], created);
    }

    [Fact]
    public void Paused_definition_rejects_a_day_change_and_creates_nothing_until_resumed()
    {
        var sim = Create(10, D(1, 10), null, D(10, 10));
        sim.Pause(D(5, 11));

        var (rejected, _) = sim.Change(D(20, 11), day: 3);

        Assert.Equal(RecurringRuleViolation.PausedDayChange, rejected.Violation);
        Assert.Equal(10, sim.Definition.DayOfMonth);
        Assert.Empty(sim.Definition.DuePeriods(sim.Registry, D(20, 11)));
        Assert.Null(sim.NextDueDate());

        var (_, resumed) = sim.Resume(D(20, 11));

        Assert.Empty(resumed);
        Assert.Equal(D(10, 12), sim.NextDueDate());
    }

    [Fact]
    public void Nc2_day_change_after_resume_creates_the_current_period_outside_the_pause()
    {
        var sim = Create(10, D(1, 10), null, D(10, 10));
        sim.Pause(D(5, 11));
        sim.Resume(D(20, 11));

        var (_, created) = sim.Change(D(20, 11), day: 3);

        Assert.Equal([D(3, 11)], created);
    }

    [Fact]
    public void Nc4_shortening_then_extending_during_a_pause_creates_nothing_from_the_pause_after_resume()
    {
        var sim = Create(10, D(1, 1), D(31, 12), D(10, 7));
        sim.Pause(D(5, 8));
        var (shortened, shortCreated) = sim.Change(D(1, 9), endsOn: D(31, 7));
        var (extended, longCreated) = sim.Change(D(1, 10), noEnd: true);

        var (_, resumed) = sim.Resume(D(20, 11));

        Assert.True(shortened.Succeeded && extended.Succeeded);
        Assert.Empty(shortCreated);
        Assert.Empty(longCreated);
        Assert.Empty(resumed);
        Assert.Equal(D(10, 7), sim.Created[^1]);
        Assert.Equal(D(10, 12), sim.NextDueDate());
    }

    [Fact]
    public void Editing_other_fields_of_a_paused_definition_takes_effect_after_resume()
    {
        var sim = Create(10, D(1, 1), D(31, 12), D(10, 9));
        sim.Pause(D(1, 10));

        var (change, created) = sim.Edit(10, D(1, 1), null, D(20, 10), Content(450m));
        var (_, resumed) = sim.Resume(D(20, 11));

        Assert.True(change.Succeeded);
        Assert.Contains(change.Changes, c => c.Field == RecurringDefinitionField.GrossAmount);
        Assert.Empty(created);
        Assert.Empty(resumed);
        var december = sim.Definition.CreateOccurrence(Guid.NewGuid(), new RecurringPeriod(2026, 12), Now);
        Assert.Equal(450m, december.Expense.GrossAmount);
        Assert.Equal(D(10, 12), sim.NextDueDate());
    }

    [Fact]
    public void Several_pauses_then_an_extension_create_only_the_periods_outside_the_pauses()
    {
        var sim = Create(10, D(1, 1), D(31, 7), D(10, 2));
        sim.Pause(D(1, 3));
        sim.Resume(D(15, 4));
        sim.Run(D(10, 5));
        sim.Pause(D(1, 6));
        sim.Resume(D(15, 7));

        var (_, created) = sim.Change(D(20, 11), noEnd: true);

        Assert.Equal([D(10, 8), D(10, 9), D(10, 10), D(10, 11)], created);
        Assert.Equal([D(10, 1), D(10, 2), D(10, 5), D(10, 8), D(10, 9), D(10, 10), D(10, 11)], sim.Created);
    }

    [Fact]
    public void Earlier_start_excludes_past_due_dates_only()
    {
        var sim = Create(10, D(1, 6), null, D(20, 11));

        var (_, created) = sim.Change(D(20, 11), startsOn: D(1, 1));

        Assert.Empty(created);
        var exclusion = Assert.Single(sim.Definition.Exclusions);
        Assert.Equal((D(1, 1), D(1, 6), RecurringExclusionReason.StartMoved), (exclusion.FromDate, exclusion.ToDate, exclusion.Reason));
    }

    [Fact]
    public void Earlier_start_partly_in_the_future_keeps_the_future_due_date()
    {
        var sim = Create(25, D(15, 12), null, D(20, 11));

        sim.Change(D(20, 11), startsOn: D(10, 11));

        var exclusion = Assert.Single(sim.Definition.Exclusions);
        Assert.Equal((D(10, 11), D(20, 11)), (exclusion.FromDate, exclusion.ToDate));
        Assert.Equal(D(25, 11), sim.NextDueDate());
    }

    [Theory]
    [InlineData(20, true)]
    [InlineData(19, false)]
    public void Earlier_start_counts_the_due_date_of_today_but_not_of_yesterday(int day, bool createdNow)
    {
        var sim = Create(day, D(1, 12), null, D(20, 11));

        var (_, created) = sim.Change(D(20, 11), startsOn: D(1, 11));

        Assert.Equal(createdNow ? [D(20, 11)] : [], created);
        Assert.Equal(createdNow ? D(20, 12) : D(19, 12), sim.NextDueDate());
    }

    [Fact]
    public void Later_start_adds_no_exclusion()
    {
        var sim = Create(10, D(1, 1), null, D(20, 11));

        var (change, _) = sim.Change(D(20, 11), startsOn: D(1, 3));

        Assert.True(change.Succeeded);
        Assert.Empty(sim.Definition.Exclusions);
    }

    [Fact]
    public void Later_then_earlier_start_does_not_bring_back_a_due_uncreated_period()
    {
        var sim = Create(10, D(1, 10), null, D(10, 10));
        var dueBefore = sim.Definition.DuePeriods(sim.Registry, D(20, 11));

        sim.Change(D(20, 11), startsOn: D(15, 11));
        var dropped = dueBefore.Count(period => !RecurringSchedule.Qualifies(sim.Definition.Schedule, sim.Registry, period));
        var (_, created) = sim.Change(D(20, 11), startsOn: D(1, 10));

        Assert.Equal(1, dropped);
        Assert.Empty(created);
        Assert.Contains(sim.Definition.Exclusions, e => e.FromDate == D(1, 10) && e.ToDate == D(15, 11));
        Assert.Equal(D(10, 12), sim.NextDueDate());
    }

    [Fact]
    public void Periods_before_the_first_day_entry_fall_inside_the_start_moved_exclusion()
    {
        var sim = Create(10, D(15, 3, 2027), null, D(20, 11));
        sim.Change(D(20, 11), startsOn: D(1, 9));

        var (_, created) = sim.Change(D(20, 11), day: 25);

        Assert.Empty(created);
        var entry = Assert.Single(sim.Definition.Days);
        Assert.Equal((D(1, 11), 25), (entry.FromPeriod, entry.DayOfMonth));
        Assert.Equal(D(25, 9), RecurringSchedule.DueDate(sim.Definition.Schedule, new RecurringPeriod(2026, 9)));
        Assert.Equal(D(25, 11), sim.NextDueDate());
    }

    [Fact]
    public void Overlapping_pause_and_start_moved_exclusions_are_summed()
    {
        var sim = Create(10, D(1, 1), null, D(10, 1));
        sim.Pause(D(1, 3));
        sim.Resume(D(15, 4));
        sim.Change(D(20, 4), startsOn: D(1, 5));
        sim.Run(D(19, 11));

        var (_, created) = sim.Change(D(20, 11), startsOn: D(1, 2));

        Assert.Empty(created);
        Assert.Equal(2, sim.Definition.Exclusions.Count);
        Assert.Equal([D(10, 1), D(10, 2), D(10, 5), D(10, 6), D(10, 7), D(10, 8), D(10, 9), D(10, 10), D(10, 11)], sim.Created);
        Assert.Empty(sim.Definition.DuePeriods(sim.Registry, D(20, 11)));
    }

    [Fact]
    public void Day_of_month_always_equals_the_last_day_entry()
    {
        var sim = Create(10, D(15, 3, 2027), null, D(20, 11));

        sim.Change(D(20, 11), day: 25);
        sim.Change(D(20, 12), day: 5);

        Assert.Equal(sim.Definition.Days.OrderBy(d => d.FromPeriod).Last().DayOfMonth, sim.Definition.DayOfMonth);
        Assert.Equal(5, sim.Definition.DayOfMonth);
        Assert.Equal(2, sim.Definition.Days.Count);
    }

    [Fact]
    public void Edit_without_changes_returns_no_changes()
    {
        var sim = Create(10, D(1, 1), D(31, 12), D(20, 11));
        var updated = sim.Definition.UpdatedAtUtc;

        var (change, created) = sim.Change(D(20, 11));

        Assert.True(change.Succeeded);
        Assert.Empty(change.Changes);
        Assert.Empty(created);
        Assert.Equal(updated, sim.Definition.UpdatedAtUtc);
    }

    [Fact]
    public void Edit_to_a_range_without_due_dates_is_rejected_without_changes()
    {
        var sim = Create(10, D(1, 1), D(31, 12), D(1, 1));

        var (change, _) = sim.Change(D(1, 1), startsOn: D(15, 3), endsOn: D(20, 3));

        Assert.Equal(RecurringRuleViolation.NoDueInRange, change.Violation);
        Assert.Equal(D(1, 1), sim.Definition.StartsOn);
    }

    [Fact]
    public void Edit_lists_changed_fields_including_day_of_month()
    {
        var sim = Create(10, D(1, 11), null, D(20, 11));

        var (change, _) = sim.Change(D(20, 11), day: 25, endsOn: D(31, 12));

        Assert.Equal(
            [
                new RecurringFieldChange(RecurringDefinitionField.DayOfMonth, 10, 25),
                new RecurringFieldChange(RecurringDefinitionField.EndsOn, null, D(31, 12)),
            ],
            change.Changes);
        Assert.Equal(D(25, 12), sim.Definition.LastDueDate);
    }

    [Fact]
    public void Amounts_must_be_positive_with_net_between_zero_and_gross()
    {
        Assert.Equal(RecurringAmountError.GrossNotPositive, RecurringRules.CheckAmounts(0m, null));
        Assert.Equal(RecurringAmountError.GrossNotPositive, RecurringRules.CheckAmounts(-1m, null));
        Assert.Equal(RecurringAmountError.NetNegative, RecurringRules.CheckAmounts(10m, -1m));
        Assert.Equal(RecurringAmountError.NetExceedsGross, RecurringRules.CheckAmounts(10m, 10.01m));
        Assert.Equal(RecurringAmountError.None, RecurringRules.CheckAmounts(10m, 0m));
        Assert.Equal(RecurringAmountError.None, RecurringRules.CheckAmounts(10m, 10m));
        Assert.Throws<ArgumentException>(() => RecurringExpenseDefinition.Create(
            Guid.NewGuid(), Content(0m), new RecurringScheduleInput(10, D(1, 1), null), Now));
    }

    [Fact]
    public void Occurrence_requires_no_payment_date()
    {
        var content = new ExpenseContent("Abonament", ExpenseCategory.Marketing, null, D(10, 1), D(10, 1), D(10, 1), 10m, null, null, null, null, null);

        Assert.Throws<ArgumentException>(() => Expense.CreateOccurrence(Guid.NewGuid(), content, Guid.NewGuid(), new RecurringPeriod(2026, 1), Now));
    }
}
