using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using StoneFlow.Reporting.Application.Sales;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Reporting.Api.Endpoints;

/// <summary>
/// Endpointy raportu sprzedaży (SF-19): <c>GET /api/reports/sales</c> (karty, szereg, kanały,
/// kategorie) i <c>GET /api/reports/sales/components</c> (zestawienie składników). Wymagają sesji
/// (polityka domyślna hosta). Bez logiki biznesowej.
/// </summary>
internal static class SalesReportEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reports/sales").WithTags("Reports");

        group.MapGet(string.Empty, GetAsync).WithName("Reporting.GetSalesReport");
        group.MapGet("/components", ListComponentsAsync).WithName("Reporting.ListSalesComponents");
    }

    private static async Task<Results<Ok<SalesReportResponse>, ValidationProblem, ProblemHttpResult>> GetAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] string? period = null,
        [FromQuery] string? year = null,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null)
    {
        var result = await dispatcher.QueryAsync(new GetSalesReportQuery(period, year, from, to), cancellationToken);

        if (result.TooManyRows)
        {
            return ReportingProblems.ReportRangeTooLarge();
        }

        return result.Report is { } report
            ? TypedResults.Ok(SalesReportContracts.ToResponse(report))
            : TypedResults.ValidationProblem(result.Errors?.ToDictionary() ?? new Dictionary<string, string[]>());
    }

    private static async Task<Results<Ok<SalesComponentsPageResponse>, ValidationProblem, ProblemHttpResult>> ListComponentsAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] string? period = null,
        [FromQuery] string? year = null,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
        [FromQuery] string? channel = null,
        [FromQuery] string? kind = null,
        [FromQuery] string? direction = null,
        [FromQuery] string? page = null,
        [FromQuery] string? pageSize = null)
    {
        var result = await dispatcher.QueryAsync(
            new ListSalesComponentsQuery(period, year, from, to, channel, kind, direction, page, pageSize),
            cancellationToken);

        if (result.TooManyRows)
        {
            return ReportingProblems.ReportRangeTooLarge();
        }

        return result.Page is { } list
            ? TypedResults.Ok(SalesReportContracts.ToResponse(list))
            : TypedResults.ValidationProblem(result.Errors?.ToDictionary() ?? new Dictionary<string, string[]>());
    }
}
