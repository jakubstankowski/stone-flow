using StoneFlow.Reporting.Application.Operations;
using StoneFlow.Reporting.Application.Sales;
using StoneFlow.Reporting.Domain.Periods;

namespace StoneFlow.Reporting.Api.Endpoints;

// Kontrakt HTTP raportu sprzedaży (SF-19): kwoty jako tekst dziesiętny z kropką i opcjonalnym
// minusem ("-1234.50"), procent z jednym miejscem po kropce ("12.5"), daty yyyy-MM-dd, liczba
// zamówień jako liczba całkowita, kanały, kategorie i rodzaje jako kody.

public sealed record SalesReportPeriodResponse(
    string Kind,
    string From,
    string To,
    string EffectiveFrom,
    string LaunchDate,
    bool BeforeLaunch,
    bool InProgress,
    string Granularity);

/// <summary>Okres bazowy porównania; <c>reason = "beforeLaunch"</c>, gdy porównanie jest niedostępne.</summary>
public sealed record SalesReportComparisonResponse(string From, string To, bool Available, string? Reason);

public sealed record AmountComparisonResponse(string Status, string? Base, string? Difference, string? Percent);

public sealed record CountComparisonResponse(string Status, int? Base, int? Difference, string? Percent);

public sealed record AmountKpiResponse(string Value, AmountComparisonResponse Comparison);

public sealed record CountKpiResponse(int Value, CountComparisonResponse Comparison);

public sealed record SalesKpisResponse(AmountKpiResponse Sales, AmountKpiResponse Costs, AmountKpiResponse Result, CountKpiResponse OrderCount);

public sealed record SeriesPointResponse(string From, string To, string Sales, string Costs, string Result);

public sealed record ChannelAmountResponse(string Channel, string Amount);

public sealed record CategoryAmountResponse(string Category, string Amount);

public sealed record SalesReportResponse(
    SalesReportPeriodResponse Period,
    SalesReportComparisonResponse Comparison,
    SalesKpisResponse? Kpis,
    IReadOnlyList<SeriesPointResponse> Series,
    IReadOnlyList<ChannelAmountResponse> Channels,
    IReadOnlyList<CategoryAmountResponse> Categories,
    DateTimeOffset CalculatedAtUtc);

/// <summary>Pozycja zestawienia; <c>amount</c> to kwota ze znakiem (zwrot i korekta ujemne).</summary>
public sealed record SalesComponentItemResponse(
    string Kind,
    Guid Id,
    string Date,
    string Amount,
    Guid OrderId,
    string OrderNumber,
    string Channel);

/// <summary>Strona zestawienia; <c>sum</c> dla całego filtra, <c>null</c> przy okresie przed uruchomieniem.</summary>
public sealed record SalesComponentsPageResponse(
    SalesReportPeriodResponse Period,
    IReadOnlyList<SalesComponentItemResponse> Items,
    int Page,
    int PageSize,
    int Total,
    string? Sum);

internal static class SalesReportContracts
{
    public static SalesReportResponse ToResponse(SalesReportDto report) => new(
        ToResponse(report.Period),
        new SalesReportComparisonResponse(
            OperationCodes.FormatDate(report.Period.Base.From),
            OperationCodes.FormatDate(report.Period.Base.To),
            report.ComparisonAvailable,
            report.ComparisonAvailable ? null : SalesReportCodes.BeforeLaunchReason),
        report.Kpis is { } kpis
            ? new SalesKpisResponse(
                ToResponse(kpis.Sales),
                ToResponse(kpis.Costs),
                ToResponse(kpis.Result),
                new CountKpiResponse(kpis.OrderCount.Value, ToCountResponse(kpis.OrderCount.Comparison)))
            : null,
        [.. report.Series.Select(point => new SeriesPointResponse(
            OperationCodes.FormatDate(point.Range.From),
            OperationCodes.FormatDate(point.Range.To),
            OperationCodes.FormatAmount(point.Sales),
            OperationCodes.FormatAmount(point.Costs),
            OperationCodes.FormatAmount(point.Result)))],
        [.. report.Channels.Select(channel => new ChannelAmountResponse(channel.Channel, OperationCodes.FormatAmount(channel.Amount)))],
        [.. report.Categories.Select(category => new CategoryAmountResponse(category.Category, OperationCodes.FormatAmount(category.Amount)))],
        report.CalculatedAtUtc.ToUniversalTime());

    public static SalesComponentsPageResponse ToResponse(SalesComponentsPageDto page) => new(
        ToResponse(page.Period),
        [.. page.Items.Select(item => new SalesComponentItemResponse(
            SalesReportCodes.Of(item.Component.Kind),
            item.Component.Id,
            OperationCodes.FormatDate(item.Component.Date),
            OperationCodes.FormatAmount(item.SignedAmount),
            item.Component.OrderId,
            item.Component.OrderNumber,
            item.Component.Channel))],
        page.Page,
        page.PageSize,
        page.Total,
        page.Sum is { } sum ? OperationCodes.FormatAmount(sum) : null);

    private static SalesReportPeriodResponse ToResponse(ReportPeriod period) => new(
        SalesReportCodes.Of(period.Kind),
        OperationCodes.FormatDate(period.Range.From),
        OperationCodes.FormatDate(period.Range.To),
        OperationCodes.FormatDate(period.EffectiveFrom),
        OperationCodes.FormatDate(period.LaunchDate),
        period.BeforeLaunch,
        period.InProgress,
        SalesReportCodes.Of(period.Granularity));

    private static AmountKpiResponse ToResponse(AmountKpiDto kpi) => new(
        OperationCodes.FormatAmount(kpi.Value),
        new AmountComparisonResponse(
            SalesReportCodes.Of(kpi.Comparison.Status),
            kpi.Comparison.Base is { } @base ? OperationCodes.FormatAmount(@base) : null,
            kpi.Comparison.Difference is { } difference ? OperationCodes.FormatAmount(difference) : null,
            Percent(kpi.Comparison)));

    // Liczba zamówień: baza i różnica są całkowite z definicji (Comparison.Of(int, int)).
    private static CountComparisonResponse ToCountResponse(Comparison comparison) => new(
        SalesReportCodes.Of(comparison.Status),
        comparison.Base is { } @base ? (int)@base : null,
        comparison.Difference is { } difference ? (int)difference : null,
        Percent(comparison));

    private static string? Percent(Comparison comparison) =>
        comparison.Percent is { } percent ? SalesReportCodes.FormatPercent(percent) : null;
}
