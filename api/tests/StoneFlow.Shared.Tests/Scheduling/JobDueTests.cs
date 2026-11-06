using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Shared.Tests.Scheduling;

public sealed class JobDueTests
{
    private static readonly JobSchedule At2300 = JobSchedule.DailyAt(new TimeOnly(23, 0));

    /// <summary>Rejestracja 10.01.2026 10:00 czasu polskiego; termin dnia 22:00 UTC.</summary>
    private static readonly DateTimeOffset Registered = Utc(1, 10, 9);

    private static DateTimeOffset Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    private static JobDueState State(
        DateTimeOffset? registered = null,
        DateTimeOffset? success = null,
        DateTimeOffset? nextAttempt = null,
        DateTimeOffset? lease = null) => new(registered ?? Registered, success, nextAttempt, lease);

    [Fact]
    public void New_job_is_not_due_before_its_first_slot() =>
        Assert.False(JobDue.IsDue(At2300, State(), Utc(1, 10, 21)));

    [Fact]
    public void New_job_is_due_after_its_first_slot() =>
        Assert.True(JobDue.IsDue(At2300, State(), Utc(1, 10, 22, 10)));

    [Fact]
    public void Job_registered_after_todays_slot_waits_for_tomorrow() =>
        Assert.Multiple(
            () => Assert.False(JobDue.IsDue(At2300, State(registered: Utc(1, 10, 22, 30)), Utc(1, 10, 23))),
            () => Assert.True(JobDue.IsDue(At2300, State(registered: Utc(1, 10, 22, 30)), Utc(1, 11, 22))));

    [Fact]
    public void Success_after_slot_ends_the_period() =>
        Assert.False(JobDue.IsDue(At2300, State(success: Utc(1, 10, 22, 5)), Utc(1, 10, 23, 30)));

    [Fact]
    public void Success_before_slot_does_not_end_the_period() =>
        Assert.True(JobDue.IsDue(At2300, State(success: Utc(1, 10, 21, 50)), Utc(1, 10, 22, 1)));

    [Fact]
    public void Several_missed_slots_make_the_job_due_once() =>
        Assert.True(JobDue.IsDue(At2300, State(success: Utc(1, 10, 22, 5)), Utc(1, 13, 8)));

    [Fact]
    public void Backoff_blocks_until_it_passes() =>
        Assert.Multiple(
            () => Assert.False(JobDue.IsDue(At2300, State(nextAttempt: Utc(1, 10, 22, 30)), Utc(1, 10, 22, 20))),
            () => Assert.True(JobDue.IsDue(At2300, State(nextAttempt: Utc(1, 10, 22, 30)), Utc(1, 10, 22, 30))));

    [Fact]
    public void Valid_lease_blocks_and_expired_lease_does_not() =>
        Assert.Multiple(
            () => Assert.False(JobDue.IsDue(At2300, State(lease: Utc(1, 10, 22, 40)), Utc(1, 10, 22, 20))),
            () => Assert.True(JobDue.IsDue(At2300, State(lease: Utc(1, 10, 22, 10)), Utc(1, 10, 22, 20))));

    [Fact]
    public void Backoff_is_15_minutes_then_one_hour() =>
        Assert.Multiple(
            () => Assert.Equal(Utc(1, 10, 22, 15), JobDue.NextAttemptAfter(1, Utc(1, 10, 22))),
            () => Assert.Equal(Utc(1, 10, 23), JobDue.NextAttemptAfter(2, Utc(1, 10, 22))),
            () => Assert.Equal(Utc(1, 10, 23), JobDue.NextAttemptAfter(7, Utc(1, 10, 22))));

    [Fact]
    public void Error_has_type_and_message_without_stack_and_is_truncated()
    {
        Exception thrown;
        try
        {
            throw new InvalidOperationException(new string('x', 2000));
        }
        catch (InvalidOperationException exception)
        {
            thrown = exception;
        }

        var description = JobDue.DescribeError(thrown);

        Assert.Equal(JobDue.MaxErrorLength, description.Length);
        Assert.StartsWith("InvalidOperationException: xxx", description, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", description, StringComparison.Ordinal);
    }
}
