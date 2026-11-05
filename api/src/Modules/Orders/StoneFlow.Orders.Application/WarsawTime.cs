namespace StoneFlow.Orders.Application;

/// <summary>Czas w strefie Europe/Warsaw: rok numeru zamówienia, „dzisiaj” dla dat wpisów.</summary>
internal static class WarsawTime
{
    private static readonly TimeZoneInfo Zone = Find();

    public static int Year(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone).Year;

    /// <summary>Dzisiejsza data w Polsce (o 23:30 UTC zimą to już następny dzień).</summary>
    public static DateOnly Today(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);

    private static TimeZoneInfo Find() =>
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Warsaw", out var zone)
            ? zone
            : TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
}
