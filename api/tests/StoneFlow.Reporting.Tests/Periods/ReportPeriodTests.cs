using StoneFlow.Reporting.Domain.Periods;

namespace StoneFlow.Reporting.Tests.Periods;

public sealed class ReportPeriodTests
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    private static DateOnly D(string text) => DateOnly.Parse(text);

    private static DateRange R(string from, string to) => new(D(from), D(to));

    private static ReportPeriod Resolve(PeriodKind kind, int? year = null, string? from = null, string? to = null, string today = "2026-11-09", string launch = "2024-01-01") =>
        ReportPeriod.Resolve(kind, year, from is null ? null : D(from), to is null ? null : D(to), D(today), D(launch));

    // RP-1 zakresy

    [Fact]
    public void Current_month_is_the_whole_calendar_month_and_in_progress()
    {
        var period = Resolve(PeriodKind.CurrentMonth);

        Assert.Equal(R("2026-11-01", "2026-11-30"), period.Range);
        Assert.Equal(D("2026-11-01"), period.EffectiveFrom);
        Assert.True(period.InProgress);
        Assert.False(period.BeforeLaunch);
    }

    [Fact]
    public void Previous_month_is_the_whole_month_before_the_current_one()
    {
        var period = Resolve(PeriodKind.PreviousMonth, today: "2026-01-15");

        Assert.Equal(R("2025-12-01", "2025-12-31"), period.Range);
        Assert.False(period.InProgress);
    }

    [Fact]
    public void Current_year_ends_today_and_earlier_year_ends_on_december_31()
    {
        var current = Resolve(PeriodKind.Year);
        var defaulted = Resolve(PeriodKind.Year, year: null);
        var earlier = Resolve(PeriodKind.Year, year: 2025);

        Assert.Equal(R("2026-01-01", "2026-11-09"), current.Range);
        Assert.True(current.InProgress);
        Assert.Equal(current, defaulted);
        Assert.Equal(R("2025-01-01", "2025-12-31"), earlier.Range);
        Assert.False(earlier.InProgress);
    }

    [Fact]
    public void Year_after_the_current_one_is_a_program_error()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Resolve(PeriodKind.Year, year: 2027));
    }

    [Fact]
    public void Custom_range_keeps_given_dates()
    {
        var period = Resolve(PeriodKind.Custom, from: "2026-09-01", to: "2026-11-15");

        Assert.Equal(R("2026-09-01", "2026-11-15"), period.Range);
        Assert.False(period.InProgress);
    }

    [Theory]
    [InlineData(null, "2026-11-30")]
    [InlineData("2026-11-02", "2026-11-01")]
    [InlineData("2026-11-01", "2027-11-10")]
    public void Invalid_custom_range_is_a_program_error(string? from, string to)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Resolve(PeriodKind.Custom, from: from, to: to));
    }

    [Fact]
    public void Effective_from_is_cut_to_launch_date_in_the_middle_of_the_month()
    {
        var period = Resolve(PeriodKind.CurrentMonth, launch: "2026-11-03");

        Assert.Equal(D("2026-11-03"), period.EffectiveFrom);
        Assert.Equal(R("2026-11-03", "2026-11-30"), period.Effective);
        Assert.False(period.BeforeLaunch);
    }

    [Fact]
    public void Year_and_custom_range_wholly_before_launch_are_before_launch()
    {
        Assert.True(Resolve(PeriodKind.Year, year: 2025, launch: "2026-11-03").BeforeLaunch);
        Assert.True(Resolve(PeriodKind.Custom, from: "2026-10-01", to: "2026-10-31", launch: "2026-11-03").BeforeLaunch);
        Assert.True(Resolve(PeriodKind.PreviousMonth, launch: "2026-11-03").BeforeLaunch);
        Assert.False(Resolve(PeriodKind.Custom, from: "2026-10-01", to: "2026-11-03", launch: "2026-11-03").BeforeLaunch);
    }

    // RP-2 okres bazowy

    [Fact]
    public void Month_is_compared_with_the_whole_previous_month()
    {
        Assert.Equal(R("2026-10-01", "2026-10-31"), Resolve(PeriodKind.CurrentMonth).Base); // 30 vs 31 dni
        Assert.Equal(R("2026-09-01", "2026-09-30"), Resolve(PeriodKind.PreviousMonth).Base);
        Assert.Equal(R("2024-01-01", "2024-01-31"), Resolve(PeriodKind.PreviousMonth, today: "2024-03-10").Base);
    }

    [Fact]
    public void Current_year_is_compared_with_the_same_part_of_the_previous_year()
    {
        Assert.Equal(R("2025-01-01", "2025-11-09"), Resolve(PeriodKind.Year).Base);
        Assert.Equal(R("2023-01-01", "2023-02-28"), Resolve(PeriodKind.Year, today: "2024-02-29", launch: "2020-01-01").Base);
    }

    [Fact]
    public void Finished_year_is_compared_with_the_whole_previous_year()
    {
        Assert.Equal(R("2024-01-01", "2024-12-31"), Resolve(PeriodKind.Year, year: 2025).Base);
    }

    [Fact]
    public void Custom_base_has_as_many_days_right_before_the_effective_start()
    {
        Assert.Equal(R("2026-10-22", "2026-10-31"), Resolve(PeriodKind.Custom, from: "2026-11-01", to: "2026-11-10").Base);

        var cut = Resolve(PeriodKind.Custom, from: "2026-10-01", to: "2026-11-10", launch: "2026-11-03");
        Assert.Equal(R("2026-10-26", "2026-11-02"), cut.Base);
        Assert.False(cut.BaseAvailable);
    }

    [Theory]
    [InlineData(PeriodKind.CurrentMonth, null, "2026-11-03")] // miesiąc uruchomienia
    [InlineData(PeriodKind.CurrentMonth, null, "2026-10-15")] // miesiąc po miesiącu uruchomienia
    [InlineData(PeriodKind.Year, null, "2026-03-01")] // rok uruchomienia
    [InlineData(PeriodKind.Year, null, "2025-06-01")] // rok po roku uruchomienia
    public void Base_starting_before_launch_is_not_available(PeriodKind kind, int? year, string launch)
    {
        Assert.False(Resolve(kind, year, launch: launch).BaseAvailable);
    }

    [Fact]
    public void Base_starting_on_launch_date_is_available()
    {
        Assert.True(Resolve(PeriodKind.CurrentMonth, launch: "2026-10-01").BaseAvailable);
        Assert.True(Resolve(PeriodKind.Year, launch: "2025-01-01").BaseAvailable);
    }

    // RP-3 agregacja

    [Fact]
    public void Months_aggregate_by_day_and_years_by_month()
    {
        Assert.Equal(Granularity.Day, Resolve(PeriodKind.CurrentMonth).Granularity);
        Assert.Equal(Granularity.Day, Resolve(PeriodKind.PreviousMonth).Granularity);
        Assert.Equal(Granularity.Month, Resolve(PeriodKind.Year).Granularity);
    }

    [Fact]
    public void Custom_range_aggregates_by_day_up_to_62_effective_days()
    {
        Assert.Equal(Granularity.Day, Resolve(PeriodKind.Custom, from: "2026-09-01", to: "2026-11-01").Granularity); // 62 dni
        Assert.Equal(Granularity.Month, Resolve(PeriodKind.Custom, from: "2026-09-01", to: "2026-11-02").Granularity); // 63 dni
        Assert.Equal(Granularity.Day, Resolve(PeriodKind.Custom, from: "2026-01-01", to: "2026-11-02", launch: "2026-09-02").Granularity); // przycięty do 62 dni
    }

    // RP-4 przedziały

    [Fact]
    public void Daily_buckets_cover_the_whole_current_month_also_after_today()
    {
        var buckets = Resolve(PeriodKind.CurrentMonth).Buckets();

        Assert.Equal(30, buckets.Count);
        Assert.Equal(R("2026-11-01", "2026-11-01"), buckets[0]);
        Assert.Equal(R("2026-11-30", "2026-11-30"), buckets[^1]);
    }

    [Fact]
    public void Current_year_buckets_end_with_the_current_month_today()
    {
        var buckets = Resolve(PeriodKind.Year).Buckets();

        Assert.Equal(11, buckets.Count);
        Assert.Equal(R("2026-01-01", "2026-01-31"), buckets[0]);
        Assert.Equal(R("2026-11-01", "2026-11-09"), buckets[^1]);
    }

    [Fact]
    public void Monthly_buckets_are_cut_to_the_effective_range_without_gaps()
    {
        var period = Resolve(PeriodKind.Custom, from: "2026-01-15", to: "2026-05-10", launch: "2026-02-10");
        var buckets = period.Buckets();

        Assert.Equal(
            [R("2026-02-10", "2026-02-28"), R("2026-03-01", "2026-03-31"), R("2026-04-01", "2026-04-30"), R("2026-05-01", "2026-05-10")],
            buckets);
        Assert.Equal(period.Effective.Days, buckets.Sum(b => b.Days));
        Assert.Equal(3, period.BucketIndexOf(D("2026-05-10")));
        Assert.Equal(0, period.BucketIndexOf(D("2026-02-10")));
    }

    [Fact]
    public void Leap_february_has_29_daily_buckets()
    {
        var period = Resolve(PeriodKind.CurrentMonth, today: "2028-02-10");

        Assert.Equal(29, period.Buckets().Count);
        Assert.Equal(R("2028-01-01", "2028-01-31"), period.Base);
    }

    [Fact]
    public void Before_launch_has_no_buckets()
    {
        Assert.Empty(Resolve(PeriodKind.Year, year: 2025, launch: "2026-11-03").Buckets());
    }

    [Fact]
    public void Bucket_index_rejects_dates_outside_the_effective_range()
    {
        var period = Resolve(PeriodKind.CurrentMonth, launch: "2026-11-03");

        Assert.Throws<ArgumentOutOfRangeException>(() => period.BucketIndexOf(D("2026-11-02")));
        Assert.Throws<ArgumentOutOfRangeException>(() => period.BucketIndexOf(D("2026-12-01")));
    }

    [Fact]
    public void For_base_counts_the_base_range_with_the_same_rules()
    {
        var period = Resolve(PeriodKind.CurrentMonth).ForBase();

        Assert.Equal(R("2026-10-01", "2026-10-31"), period.Range);
        Assert.Equal(D("2026-10-01"), period.EffectiveFrom);
        Assert.Equal(31, period.Buckets().Count);
    }

    // RP-5 górna granica

    [Fact]
    public void Latest_end_is_one_year_from_today()
    {
        Assert.Equal(D("2027-11-09"), ReportPeriod.LatestEnd(Today));
        Assert.Equal(D("2029-02-28"), ReportPeriod.LatestEnd(D("2028-02-29")));
    }
}
