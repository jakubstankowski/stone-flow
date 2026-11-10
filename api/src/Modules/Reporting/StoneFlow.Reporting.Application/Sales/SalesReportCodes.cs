using System.Globalization;
using StoneFlow.Reporting.Domain.Periods;
using StoneFlow.Shared.Kernel.Sales;

namespace StoneFlow.Reporting.Application.Sales;

/// <summary>
/// Kody raportu sprzedaży w API (jedno miejsce mapowania): okresy, agregacja, rodzaje składników i
/// stan porównania; format procentu (<c>12.5</c>) w kulturze niezmiennej. Kwoty i daty formatuje
/// <see cref="Operations.OperationCodes"/>. Kody są porównywane dokładnie.
/// </summary>
public static class SalesReportCodes
{
    /// <summary>Powód braku porównania: okres bazowy zaczyna się przed datą uruchomienia.</summary>
    public const string BeforeLaunchReason = "beforeLaunch";

    private static readonly Dictionary<PeriodKind, string> Periods = new()
    {
        [PeriodKind.CurrentMonth] = "currentMonth",
        [PeriodKind.PreviousMonth] = "previousMonth",
        [PeriodKind.Year] = "year",
        [PeriodKind.Custom] = "custom",
    };

    private static readonly Dictionary<Granularity, string> Granularities = new()
    {
        [Granularity.Day] = "day",
        [Granularity.Month] = "month",
    };

    private static readonly Dictionary<SalesComponentKind, string> Kinds = new()
    {
        [SalesComponentKind.Order] = "order",
        [SalesComponentKind.SalesReturn] = "salesReturn",
        [SalesComponentKind.SalesCorrection] = "salesCorrection",
    };

    private static readonly Dictionary<ComparisonStatus, string> Statuses = new()
    {
        [ComparisonStatus.Available] = "available",
        [ComparisonStatus.NoBasis] = "noBasis",
    };

    public static string Of(PeriodKind kind) => Periods[kind];

    public static string Of(Granularity granularity) => Granularities[granularity];

    public static string Of(SalesComponentKind kind) => Kinds[kind];

    public static string Of(ComparisonStatus status) => Statuses[status];

    public static bool TryParse(string? code, out PeriodKind kind) => TryParse(Periods, code, out kind);

    public static bool TryParse(string? code, out SalesComponentKind kind) => TryParse(Kinds, code, out kind);

    /// <summary>Procent z jednym miejscem po kropce i opcjonalnym minusem: <c>12.5</c>, <c>-3.0</c>, <c>0.0</c>.</summary>
    public static string FormatPercent(decimal percent) => percent.ToString("0.0", CultureInfo.InvariantCulture);

    private static bool TryParse<T>(Dictionary<T, string> codes, string? code, out T value)
        where T : struct, Enum
    {
        foreach (var (key, text) in codes)
        {
            if (string.Equals(text, code, StringComparison.Ordinal))
            {
                value = key;
                return true;
            }
        }

        value = default;
        return false;
    }
}
