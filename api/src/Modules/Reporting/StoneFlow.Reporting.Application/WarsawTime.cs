using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Reporting.Application;

/// <summary>„Dzisiaj” w strefie Europe/Warsaw (współdzielona strefa <see cref="WarsawTimeZone"/>).</summary>
internal static class WarsawTime
{
    /// <summary>Dzisiejsza data w Polsce (o 23:30 UTC zimą to już następny dzień).</summary>
    public static DateOnly Today(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), WarsawTimeZone.Zone).DateTime);
    }
}
