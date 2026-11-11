using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using StoneFlow.Reporting.Application.CashFlow;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Reporting.Api.Endpoints;

/// <summary>
/// Endpoint raportu przepływów pieniężnych (SF-20): <c>GET /api/reports/cash-flow</c> (karty,
/// szereg, podział wypływów, ostatnie operacje). Parametry okresu jak w raporcie sprzedaży. Wymaga
/// sesji (polityka domyślna hosta). Bez logiki biznesowej.
/// </summary>
internal static class CashFlowReportEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reports/cash-flow").WithTags("Reports");

        group.MapGet(string.Empty, GetAsync).WithName("Reporting.GetCashFlowReport");
    }

    private static async Task<Results<Ok<CashFlowReportResponse>, ValidationProblem, ProblemHttpResult>> GetAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] string? period = null,
        [FromQuery] string? year = null,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null)
    {
        var result = await dispatcher.QueryAsync(new GetCashFlowReportQuery(period, year, from, to), cancellationToken);

        if (result.TooManyRows)
        {
            return ReportingProblems.ReportRangeTooLarge();
        }

        return result.Report is { } report
            ? TypedResults.Ok(CashFlowReportContracts.ToResponse(report))
            : TypedResults.ValidationProblem(result.Errors?.ToDictionary() ?? new Dictionary<string, string[]>());
    }
}
