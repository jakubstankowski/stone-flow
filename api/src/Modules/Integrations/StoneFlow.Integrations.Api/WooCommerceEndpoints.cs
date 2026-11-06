using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using StoneFlow.Integrations.Application.WooCommerce.Errors;
using StoneFlow.Integrations.Application.WooCommerce.Status;
using StoneFlow.Integrations.Application.WooCommerce.Sync;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Integrations.Api;

/// <summary>
/// Endpointy <c>/api/integrations/woocommerce</c> (SF-13) dla SF-14: stan, lista błędów, „Ponów”,
/// „Synchronizuj teraz”. Wymagają sesji (polityka domyślna hosta), zapisy tokenu XSRF (globalny
/// middleware). Wywołują tylko dyspozytor.
/// </summary>
internal static class WooCommerceEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/integrations/woocommerce").WithTags("Integrations");

        group.MapGet("/status", StatusAsync).WithName("Integrations.WooCommerceStatus");
        group.MapGet("/errors", ErrorsAsync).WithName("Integrations.WooCommerceErrors");
        group.MapPost("/errors/{id:guid}/retry", RetryAsync).WithName("Integrations.WooCommerceRetry");
        group.MapPost("/sync", SyncAsync).WithName("Integrations.WooCommerceSync");
    }

    private static async Task<Ok<SyncStatusResponse>> StatusAsync(IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var status = await dispatcher.QueryAsync(new GetSyncStatusQuery(), cancellationToken);
        return TypedResults.Ok(new SyncStatusResponse(
            status.Configured,
            Utc(status.LastSuccessAtUtc),
            status.LastRun is { } run
                ? new SyncRunResponse(run.Status, Utc(run.StartedAtUtc), Utc(run.FinishedAtUtc), run.Source, run.OrdersProcessed, run.Errors, run.Message)
                : null,
            status.IsRunning,
            status.OpenErrors));
    }

    private static async Task<Results<Ok<OrderErrorListResponse>, ValidationProblem>> ErrorsAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] string? status = null,
        [FromQuery] string? page = null,
        [FromQuery] string? pageSize = null)
    {
        var result = await dispatcher.QueryAsync(new ListOrderErrorsQuery(status, page, pageSize), cancellationToken);
        if (result.Page is not { } list)
        {
            return TypedResults.ValidationProblem(result.Errors ?? new Dictionary<string, string[]>());
        }

        return TypedResults.Ok(new OrderErrorListResponse([.. list.Items.Select(ToResponse)], list.Page, list.PageSize, list.Total));
    }

    private static async Task<Results<Ok<OrderErrorResponse>, ProblemHttpResult>> RetryAsync(
        Guid id,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var correlationId = Activity.Current?.TraceId.ToHexString();
        var result = await dispatcher.SendAsync(new RetryOrderErrorCommand(id, correlationId), cancellationToken);
        return result.Outcome switch
        {
            RetryOrderErrorOutcome.Done => TypedResults.Ok(ToResponse(result.Error!)),
            RetryOrderErrorOutcome.NotFound => IntegrationsProblems.ErrorNotFound(),
            RetryOrderErrorOutcome.Closed => IntegrationsProblems.ErrorClosed(),
            RetryOrderErrorOutcome.NotConfigured => IntegrationsProblems.NotConfigured(),
            RetryOrderErrorOutcome.ShopFailed => IntegrationsProblems.RetryFailed(),
            _ => IntegrationsProblems.RetryUnavailable(),
        };
    }

    private static async Task<Results<Accepted, ProblemHttpResult>> SyncAsync(IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var outcome = await dispatcher.SendAsync(new RequestSyncCommand(), cancellationToken);
        return outcome switch
        {
            RequestSyncOutcome.Accepted => TypedResults.Accepted((string?)null),
            RequestSyncOutcome.NotConfigured => IntegrationsProblems.NotConfigured(),
            _ => IntegrationsProblems.SyncInProgress(),
        };
    }

    private static OrderErrorResponse ToResponse(OrderErrorItem item)
    {
        var error = item.Error;
        return new OrderErrorResponse(
            error.Id,
            error.ExternalId,
            item.OrderId,
            item.OrderNumber,
            error.Status,
            error.Source,
            Utc(error.FirstOccurredAtUtc),
            Utc(error.LastOccurredAtUtc),
            error.Attempts,
            error.Message,
            error.Context,
            Utc(error.ClosedAtUtc),
            error.ClosedBy);
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? Utc(DateTime? value) => value is { } v ? Utc(v) : null;
}

/// <summary>Stan synchronizacji; <c>lastRun.status</c>: <c>running</c>, <c>succeeded</c>, <c>failed</c>, <c>abandoned</c> (jak nieudany).</summary>
public sealed record SyncStatusResponse(
    bool Configured,
    DateTimeOffset? LastSuccessAtUtc,
    SyncRunResponse? LastRun,
    bool IsRunning,
    int OpenErrors);

/// <summary>Ostatni przebieg uzupełniania; <c>source</c>: <c>schedule</c> albo <c>manual</c>.</summary>
public sealed record SyncRunResponse(
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string Source,
    int OrdersProcessed,
    int Errors,
    string? Message);

/// <summary>Błąd zamówienia; <c>status</c>: <c>open</c>/<c>closed</c>, <c>source</c> i <c>closedBy</c>: <c>backfill</c>/<c>webhook</c>/<c>retry</c>.</summary>
public sealed record OrderErrorResponse(
    Guid Id,
    string ExternalId,
    Guid? OrderId,
    string? OrderNumber,
    string Status,
    string Source,
    DateTimeOffset FirstOccurredAtUtc,
    DateTimeOffset LastOccurredAtUtc,
    int Attempts,
    string Message,
    string Context,
    DateTimeOffset? ClosedAtUtc,
    string? ClosedBy);

public sealed record OrderErrorListResponse(IReadOnlyList<OrderErrorResponse> Items, int Page, int PageSize, int Total);
