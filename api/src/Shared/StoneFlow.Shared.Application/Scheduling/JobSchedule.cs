using System.Globalization;

namespace StoneFlow.Shared.Application.Scheduling;

/// <summary>Harmonogram dzienny: jeden termin dziennie o stałej godzinie w strefie Europe/Warsaw.</summary>
public sealed record JobSchedule
{
    private JobSchedule(TimeOnly localTime) => LocalTime = localTime;

    public TimeOnly LocalTime { get; }

    public static JobSchedule DailyAt(TimeOnly localTime) => new(localTime);

    /// <summary>Opis do API, np. <c>daily 23:00 Europe/Warsaw</c>.</summary>
    public string Describe() => $"daily {LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture)} Europe/Warsaw";

    /// <summary>Najpóźniejszy termin nie późniejszy niż <paramref name="instant"/>.</summary>
    public DateTimeOffset LatestSlotAtOrBefore(DateTimeOffset instant)
    {
        var date = LocalDate(instant);
        var slot = SlotOn(date);
        return slot <= instant ? slot : SlotOn(date.AddDays(-1));
    }

    /// <summary>Najwcześniejszy termin nie wcześniejszy niż <paramref name="instant"/>.</summary>
    public DateTimeOffset FirstSlotAtOrAfter(DateTimeOffset instant)
    {
        var date = LocalDate(instant);
        var slot = SlotOn(date);
        return slot >= instant ? slot : SlotOn(date.AddDays(1));
    }

    /// <summary>
    /// Termin w danym dniu jako chwila UTC. Godzina z przerwy wiosennej przesuwa się na pierwszą
    /// istniejącą chwilę po przerwie; godzina powtórzona jesienią liczy się przy pierwszym wystąpieniu.
    /// </summary>
    public DateTimeOffset SlotOn(DateOnly localDate)
    {
        var zone = WarsawTimeZone.Zone;
        var local = localDate.ToDateTime(LocalTime, DateTimeKind.Unspecified);

        // Przerwa wiosenna: 02:30 → 03:00 czasu letniego (pierwsza istniejąca minuta).
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    private static DateOnly LocalDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, WarsawTimeZone.Zone).DateTime);
}
