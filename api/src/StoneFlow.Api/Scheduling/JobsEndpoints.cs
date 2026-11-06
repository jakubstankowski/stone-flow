using Microsoft.AspNetCore.Http.HttpResults;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Api.Scheduling;

/// <summary>
/// <c>GET /api/jobs</c>: stan zadań automatycznych (SF-11). Wymaga sesji (polityka domyślna hosta).
/// Endpoint należy do hosta, bo harmonogram nie jest częścią żadnego modułu.
/// </summary>
internal static class JobsEndpoints
{
    public static IEndpointRouteBuilder MapJobsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/jobs", ListAsync).WithTags("Jobs").WithName("Jobs.List");
        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<JobStatusDto>>> ListAsync(IDispatcher dispatcher, CancellationToken cancellationToken) =>
        TypedResults.Ok(await dispatcher.QueryAsync(new GetJobStatusesQuery(), cancellationToken));
}
