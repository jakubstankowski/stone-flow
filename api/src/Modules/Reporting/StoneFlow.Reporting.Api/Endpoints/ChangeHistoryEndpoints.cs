using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using StoneFlow.Reporting.Application.ChangeHistory;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Reporting.Api.Endpoints;

/// <summary>
/// Endpoint <c>GET /api/change-history</c> (SF-18): stronicowana lista wpisów audytu tylko do
/// odczytu. Wymaga sesji (polityka domyślna hosta). Brak tras zapisu: inne metody dają 405.
/// Bez logiki biznesowej.
/// </summary>
internal static class ChangeHistoryEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/change-history").WithTags("ChangeHistory");

        group.MapGet(string.Empty, ListAsync).WithName("Reporting.ListChangeHistory");
    }

    private static async Task<Results<Ok<ChangeHistoryPageResponse>, ValidationProblem>> ListAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] string? changedFrom = null,
        [FromQuery] string? changedTo = null,
        [FromQuery] string? businessFrom = null,
        [FromQuery] string? businessTo = null,
        [FromQuery] string[]? objectType = null,
        [FromQuery] string? direction = null,
        [FromQuery] string? page = null,
        [FromQuery] string? pageSize = null)
    {
        var result = await dispatcher.QueryAsync(
            new ListChangeHistoryQuery(changedFrom, changedTo, businessFrom, businessTo, objectType, direction, page, pageSize),
            cancellationToken);

        return result.Page is { } list
            ? TypedResults.Ok(ChangeHistoryContracts.ToResponse(list))
            : TypedResults.ValidationProblem(result.Errors?.ToDictionary() ?? new Dictionary<string, string[]>());
    }
}
