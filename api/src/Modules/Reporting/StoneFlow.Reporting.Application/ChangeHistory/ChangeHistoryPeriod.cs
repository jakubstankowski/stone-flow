using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Reporting.Application.ChangeHistory;

/// <summary>
/// Okres zmiany w Europe/Warsaw jako przedział UTC <c>[from 00:00, to+1 00:00)</c>, z uwzględnieniem
/// czasu letniego. Północ w Europe/Warsaw nigdy nie wypada w przerwie ani w powtórzonej godzinie
/// (zmiany czasu o 02:00/03:00), więc wystarcza zwykła konwersja.
/// </summary>
public static class ChangeHistoryPeriod
{
    public static (DateTime FromUtc, DateTime ToUtcExclusive) ToUtcRange(DateOnly from, DateOnly to) =>
        (StartOfDayUtc(from), StartOfDayUtc(to.AddDays(1)));

    private static DateTime StartOfDayUtc(DateOnly date) =>
        DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), WarsawTimeZone.Zone),
            DateTimeKind.Utc);
}
