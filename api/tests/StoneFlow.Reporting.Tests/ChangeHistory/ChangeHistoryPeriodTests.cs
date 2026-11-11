using StoneFlow.Reporting.Application.ChangeHistory;

namespace StoneFlow.Reporting.Tests.ChangeHistory;

/// <summary>Okres zmiany w Europe/Warsaw jako przedział UTC <c>[od 00:00, do+1 00:00)</c>, także przy zmianie czasu.</summary>
public sealed class ChangeHistoryPeriodTests
{
    [Fact]
    public void Winter_day_starts_and_ends_at_23_00_utc()
    {
        var (from, to) = ChangeHistoryPeriod.ToUtcRange(new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 5));

        Assert.Equal(new DateTime(2026, 11, 4, 23, 0, 0, DateTimeKind.Utc), from);
        Assert.Equal(new DateTime(2026, 11, 5, 23, 0, 0, DateTimeKind.Utc), to);
        Assert.Equal(DateTimeKind.Utc, from.Kind);
        Assert.Equal(DateTimeKind.Utc, to.Kind);
    }

    [Fact]
    public void End_of_summer_time_day_has_25_hours()
    {
        var (from, to) = ChangeHistoryPeriod.ToUtcRange(new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 25));

        Assert.Equal(new DateTime(2026, 10, 24, 22, 0, 0, DateTimeKind.Utc), from);
        Assert.Equal(new DateTime(2026, 10, 25, 23, 0, 0, DateTimeKind.Utc), to);
        Assert.Equal(TimeSpan.FromHours(25), to - from);
    }

    [Fact]
    public void Start_of_summer_time_day_has_23_hours()
    {
        var (from, to) = ChangeHistoryPeriod.ToUtcRange(new DateOnly(2026, 3, 29), new DateOnly(2026, 3, 29));

        Assert.Equal(new DateTime(2026, 3, 28, 23, 0, 0, DateTimeKind.Utc), from);
        Assert.Equal(new DateTime(2026, 3, 29, 22, 0, 0, DateTimeKind.Utc), to);
        Assert.Equal(TimeSpan.FromHours(23), to - from);
    }

    [Fact]
    public void Multi_day_period_covers_every_day_up_to_the_next_midnight()
    {
        var (from, to) = ChangeHistoryPeriod.ToUtcRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        Assert.Equal(new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc), from);
        Assert.Equal(new DateTime(2026, 10, 31, 23, 0, 0, DateTimeKind.Utc), to);
    }

    [Fact]
    public void Entry_at_23_30_polish_time_belongs_to_its_own_day()
    {
        // 05.11.2026 23:30 czasu polskiego = 22:30 UTC.
        var occurred = new DateTime(2026, 11, 5, 22, 30, 0, DateTimeKind.Utc);
        var (from, to) = ChangeHistoryPeriod.ToUtcRange(new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 5));
        var (nextFrom, _) = ChangeHistoryPeriod.ToUtcRange(new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 6));

        Assert.True(occurred >= from && occurred < to);
        Assert.True(occurred < nextFrom);
    }
}
