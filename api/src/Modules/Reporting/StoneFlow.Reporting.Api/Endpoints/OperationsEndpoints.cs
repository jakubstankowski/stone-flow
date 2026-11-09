using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using StoneFlow.Reporting.Application.Operations;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Reporting.Api.Endpoints;

/// <summary>
/// Endpoint <c>GET /api/operations</c> (SF-17): lista operacji pieniężnych od daty uruchomienia z
/// sumami dla filtra. Wymaga sesji (polityka domyślna hosta). Bez logiki biznesowej.
/// </summary>
internal static class OperationsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/operations").WithTags("Operations");

        group.MapGet(string.Empty, ListAsync).WithName("Reporting.ListOperations");
    }

    private static async Task<Results<Ok<OperationsPageResponse>, ValidationProblem, ProblemHttpResult>> ListAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
        [FromQuery] string[]? kind = null,
        [FromQuery] string? direction = null,
        [FromQuery] string? page = null,
        [FromQuery] string? pageSize = null)
    {
        var result = await dispatcher.QueryAsync(new ListOperationsQuery(from, to, kind, direction, page, pageSize), cancellationToken);

        if (result.TooManyOperations)
        {
            return ReportingProblems.RangeTooLarge();
        }

        return result.Page is { } list
            ? TypedResults.Ok(OperationContracts.ToResponse(list))
            : TypedResults.ValidationProblem(result.Errors?.ToDictionary() ?? new Dictionary<string, string[]>());
    }
}
