using System.Globalization;
using StoneFlow.Reporting.Application.Abstractions;
using StoneFlow.Reporting.Application.Operations;
using StoneFlow.Reporting.Domain.Periods;

namespace StoneFlow.Reporting.Application.Sales;

/// <summary>
/// Walidacja wyboru okresu z zapytania HTTP, wspólna dla raportu sprzedaży i zestawienia
/// składników (SF-19). Parametry niepotrzebne dla danego okresu są ignorowane.
/// </summary>
internal static class ReportPeriodInput
{
    /// <summary>
    /// Domyślnie bieżący miesiąc; rok domyślnie bieżący i nie późniejszy; własny zakres wymaga dat
    /// <c>from ≤ to ≤ dziś + 1 rok</c>. Błędy trafiają do <paramref name="errors"/>, wynik to
    /// <c>null</c>.
    /// </summary>
    public static ReportPeriod? Validate(
        string? period,
        string? year,
        string? from,
        string? to,
        DateOnly today,
        DateOnly launchDate,
        FieldErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var kind = PeriodKind.CurrentMonth;
        if (Text(period) is { } periodText && !SalesReportCodes.TryParse(periodText, out kind))
        {
            errors.Add("period", "Nieznany okres.");
            return null;
        }

        int? selectedYear = null;
        DateOnly? start = null;
        DateOnly? end = null;
        switch (kind)
        {
            case PeriodKind.Year:
                selectedYear = Year(year, today, errors);
                break;
            case PeriodKind.Custom:
                start = RequiredDate(from, "from", "Podaj datę początkową.", errors);
                end = RequiredDate(to, "to", "Podaj datę końcową.", errors);
                if (start > end)
                {
                    errors.Add("from", "Data początkowa nie może być późniejsza niż końcowa.");
                }

                var latest = ReportPeriod.LatestEnd(today);
                if (end > latest)
                {
                    errors.Add("to", $"Data końcowa nie może być późniejsza niż {latest.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)} (rok od dziś).");
                }

                break;
        }

        return errors.IsEmpty
            ? ReportPeriod.Resolve(kind, selectedYear, start, end, today, launchDate)
            : null;
    }

    private static int? Year(string? text, DateOnly today, FieldErrors errors)
    {
        if (Text(text) is not { } trimmed)
        {
            return today.Year;
        }

        if (trimmed.Length != 4
            || !int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || year < 1000)
        {
            errors.Add("year", "Podaj rok jako liczbę czterocyfrową.");
            return null;
        }

        if (year > today.Year)
        {
            errors.Add("year", "Rok nie może być późniejszy niż bieżący.");
            return null;
        }

        return year;
    }

    private static DateOnly? RequiredDate(string? text, string field, string missing, FieldErrors errors)
    {
        if (Text(text) is not { } trimmed)
        {
            errors.Add(field, missing);
            return null;
        }

        if (OperationCodes.TryParseDate(trimmed, out var date))
        {
            return date;
        }

        errors.Add(field, "Podaj datę w formacie RRRR-MM-DD.");
        return null;
    }

    internal static string? Text(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
