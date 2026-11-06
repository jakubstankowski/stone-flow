namespace StoneFlow.Shared.Application.Scheduling;

/// <summary>Strefa Europe/Warsaw; na Windows bez danych IANA rezerwowy identyfikator Windows.</summary>
public static class WarsawTimeZone
{
    public static TimeZoneInfo Zone { get; } =
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Warsaw", out var zone)
            ? zone
            : TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
}
