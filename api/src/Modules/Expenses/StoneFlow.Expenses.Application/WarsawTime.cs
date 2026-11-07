using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Expenses.Application;

/// <summary>„Dzisiaj” w strefie Europe/Warsaw (współdzielona strefa <see cref="WarsawTimeZone"/>).</summary>
public static class WarsawTime
{
    /// <summary>Dzisiejsza data w Polsce (o 23:30 UTC zimą to już następny dzień).</summary>
    public static DateOnly Today(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, WarsawTimeZone.Zone).DateTime);

    public static DateOnly Today(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return Today(timeProvider.GetUtcNow());
    }
}
