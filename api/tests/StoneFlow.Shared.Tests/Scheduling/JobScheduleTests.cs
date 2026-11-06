using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Shared.Tests.Scheduling;

/// <summary>Terminy dzienne w Europe/Warsaw (2026: zmiana czasu 29.03 i 25.10).</summary>
public sealed class JobScheduleTests
{
    private static readonly JobSchedule At2300 = JobSchedule.DailyAt(new TimeOnly(23, 0));

    private static DateTimeOffset Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void Winter_slot_is_2200_utc() =>
        Assert.Equal(Utc(1, 10, 22), At2300.LatestSlotAtOrBefore(Utc(1, 10, 22, 30)));

    [Fact]
    public void Summer_slot_is_2100_utc() =>
        Assert.Equal(Utc(7, 10, 21), At2300.LatestSlotAtOrBefore(Utc(7, 10, 21, 30)));

    [Fact]
    public void Before_todays_slot_the_latest_is_yesterday() =>
        Assert.Equal(Utc(1, 9, 22), At2300.LatestSlotAtOrBefore(Utc(1, 10, 21, 30)));

    [Fact]
    public void After_midnight_in_poland_the_latest_is_previous_local_day() =>
        Assert.Equal(Utc(1, 10, 22), At2300.LatestSlotAtOrBefore(Utc(1, 10, 23, 30)));

    [Fact]
    public void Exactly_at_slot_counts_as_latest_and_first() =>
        Assert.Multiple(
            () => Assert.Equal(Utc(1, 10, 22), At2300.LatestSlotAtOrBefore(Utc(1, 10, 22))),
            () => Assert.Equal(Utc(1, 10, 22), At2300.FirstSlotAtOrAfter(Utc(1, 10, 22))));

    [Fact]
    public void First_slot_after_registration() =>
        Assert.Multiple(
            () => Assert.Equal(Utc(1, 10, 22), At2300.FirstSlotAtOrAfter(Utc(1, 10, 9))),
            () => Assert.Equal(Utc(1, 11, 22), At2300.FirstSlotAtOrAfter(Utc(1, 10, 22, 1))));

    [Fact]
    public void Slot_on_dst_change_days_keeps_local_hour() =>
        Assert.Multiple(
            () => Assert.Equal(Utc(3, 28, 22), At2300.SlotOn(new DateOnly(2026, 3, 28))),
            () => Assert.Equal(Utc(3, 29, 21), At2300.SlotOn(new DateOnly(2026, 3, 29))),
            () => Assert.Equal(Utc(10, 24, 21), At2300.SlotOn(new DateOnly(2026, 10, 24))),
            () => Assert.Equal(Utc(10, 25, 22), At2300.SlotOn(new DateOnly(2026, 10, 25))));

    [Fact]
    public void Hour_in_spring_gap_moves_to_first_existing_minute() =>
        Assert.Equal(Utc(3, 29, 1), JobSchedule.DailyAt(new TimeOnly(2, 30)).SlotOn(new DateOnly(2026, 3, 29)));

    [Fact]
    public void Repeated_autumn_hour_uses_first_occurrence() =>
        Assert.Equal(Utc(10, 25, 0, 30), JobSchedule.DailyAt(new TimeOnly(2, 30)).SlotOn(new DateOnly(2026, 10, 25)));

    [Fact]
    public void Describes_schedule() =>
        Assert.Equal("daily 23:00 Europe/Warsaw", At2300.Describe());
}
