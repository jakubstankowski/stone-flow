using StoneFlow.Integrations.Domain.WooCommerce;
using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Integrations.Tests.WooCommerce;

/// <summary>SF-13: okno uzupełniania i warunek zamknięcia błędu zamówienia.</summary>
public sealed class WooCommerceRulesTests
{
    private static readonly DateOnly Launch = new(2026, 11, 3);

    [Fact]
    public void First_window_starts_at_launch_midnight_in_warsaw_and_ends_ten_minutes_before_start()
    {
        var started = new DateTime(2026, 11, 6, 22, 0, 0, DateTimeKind.Utc);

        var window = BackfillWindow.For(null, Launch, started, WarsawTimeZone.Zone);

        Assert.Equal(new DateTime(2026, 11, 2, 23, 0, 0, DateTimeKind.Utc), window.FromUtc);
        Assert.Equal(new DateTime(2026, 11, 6, 21, 50, 0, DateTimeKind.Utc), window.ToUtc);
        Assert.False(window.IsEmpty);
    }

    [Fact]
    public void Later_window_starts_ten_minutes_before_cursor()
    {
        var cursor = new DateTime(2026, 11, 5, 21, 50, 0, DateTimeKind.Utc);
        var started = new DateTime(2026, 11, 8, 22, 0, 0, DateTimeKind.Utc);

        var window = BackfillWindow.For(cursor, Launch, started, WarsawTimeZone.Zone);

        Assert.Equal(new DateTime(2026, 11, 5, 21, 40, 0, DateTimeKind.Utc), window.FromUtc);
        Assert.Equal(new DateTime(2026, 11, 8, 21, 50, 0, DateTimeKind.Utc), window.ToUtc);
    }

    [Fact]
    public void Run_right_after_previous_has_empty_window()
    {
        var cursor = new DateTime(2026, 11, 5, 21, 50, 0, DateTimeKind.Utc);

        Assert.True(BackfillWindow.For(cursor, Launch, cursor, WarsawTimeZone.Zone).IsEmpty);
    }

    [Theory]
    [InlineData(null, null, false, true)]
    [InlineData(null, "2026-11-05T10:00:00", false, true)]
    [InlineData("2026-11-05T10:00:00", "2026-11-05T10:00:00", false, true)]
    [InlineData("2026-11-05T10:00:00", "2026-11-05T11:00:00", false, true)]
    [InlineData("2026-11-05T10:00:00", "2026-11-05T09:00:00", false, false)]
    [InlineData("2026-11-05T10:00:00", null, false, false)]
    [InlineData("2026-11-05T10:00:00", null, true, true)]
    [InlineData("2026-11-05T10:00:00", "2026-11-05T09:00:00", true, true)]
    public void Success_closes_error_unless_older_than_failed_delivery(string? failed, string? success, bool deletion, bool closes) =>
        Assert.Equal(closes, OrderErrorRules.CanClose(Parse(failed), Parse(success), deletion));

    [Fact]
    public void Later_failure_keeps_greater_modification_date()
    {
        var earlier = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
        var later = earlier.AddHours(1);

        Assert.Equal(later, OrderErrorRules.LaterModified(earlier, later));
        Assert.Equal(later, OrderErrorRules.LaterModified(later, earlier));
        Assert.Equal(earlier, OrderErrorRules.LaterModified(null, earlier));
        Assert.Equal(earlier, OrderErrorRules.LaterModified(earlier, null));
    }

    private static DateTime? Parse(string? text) =>
        text is null ? null : DateTime.SpecifyKind(DateTime.Parse(text, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);
}
