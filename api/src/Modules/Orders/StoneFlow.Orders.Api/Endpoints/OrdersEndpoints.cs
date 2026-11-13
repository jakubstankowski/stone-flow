using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using StoneFlow.Orders.Application;
using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Application.AnonymizeCustomer;
using StoneFlow.Orders.Application.CancelOrder;
using StoneFlow.Orders.Application.CreateOrder;
using StoneFlow.Orders.Application.EditOrder;
using StoneFlow.Orders.Application.GetOrder;
using StoneFlow.Orders.Application.ListOrders;
using StoneFlow.Orders.Application.RestoreOrder;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Orders.Api.Endpoints;

/// <summary>
/// Endpointy <c>/api/orders</c>. Bez logiki biznesowej: każde wywołanie przez dyspozytor, wynik
/// mapowany na odpowiedź HTTP. Wszystkie wymagają sesji (polityka domyślna hosta), a zapisy tokenu
/// XSRF (globalny middleware). Usuwania zamówień nie ma: <c>DELETE /api/orders/{id}</c> nie jest
/// zmapowany (usuwać można tylko wpłaty, zwroty i korekty sprzedaży, <see cref="PaymentsEndpoints"/>).
/// </summary>
internal static class OrdersEndpoints
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/orders").WithTags("Orders");

        group.MapGet(string.Empty, ListAsync).WithName("Orders.List");
        group.MapGet("/{id:guid}", GetAsync).WithName("Orders.Get");
        group.MapPost(string.Empty, CreateAsync).WithName("Orders.Create");
        group.MapPut("/{id:guid}", EditAsync).WithName("Orders.Edit");
        group.MapPost("/{id:guid}/cancel", CancelAsync).WithName("Orders.Cancel");
        group.MapPost("/{id:guid}/restore", RestoreAsync).WithName("Orders.Restore");
        group.MapPost("/{id:guid}/anonymize", AnonymizeAsync).WithName("Orders.AnonymizeCustomer");
    }

    private static async Task<Results<Ok<OrderListPageResponse>, ValidationProblem>> ListAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] string? page = null,
        [FromQuery] string? pageSize = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? direction = null,
        [FromQuery] string? dateFrom = null,
        [FromQuery] string? dateTo = null,
        [FromQuery] string? channel = null,
        [FromQuery] string? source = null,
        [FromQuery] string[]? status = null,
        [FromQuery] string? search = null,
        [FromQuery] string[]? paymentState = null,
        [FromQuery] string? includeHiddenShopStatuses = null)
    {
        var result = await dispatcher.QueryAsync(
            new ListOrdersQuery(page, pageSize, sort, direction, dateFrom, dateTo, channel, source, status, search, paymentState, includeHiddenShopStatuses),
            cancellationToken);

        return result.Page is { } list
            ? TypedResults.Ok(OrderContracts.ToResponse(list))
            : ValidationProblem(result.Errors);
    }

    private static async Task<Results<Ok<OrderDetailsResponse>, ProblemHttpResult>> GetAsync(
        Guid id, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var order = await dispatcher.QueryAsync(new GetOrderQuery(id), cancellationToken);
        return order is null ? OrderProblems.NotFound() : TypedResults.Ok(OrderContracts.ToResponse(order));
    }

    private static async Task<Results<Created<OrderCreatedResponse>, Ok<OrderCreatedResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> CreateAsync(
        OrderRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken)
    {
        if (CurrentAccount(context.User) is not { } accountId)
        {
            return TypedResults.Unauthorized();
        }

        if (IdempotencyKey(context.Request) is not { } key)
        {
            return OrderProblems.IdempotencyKeyRequired();
        }

        var result = await dispatcher.SendAsync(new CreateOrderCommand(accountId, key, OrderContracts.ToInput(request)), cancellationToken);
        switch (result.Outcome)
        {
            case CreateOrderOutcome.Created:
                var created = Created(result);
                return TypedResults.Created($"/api/orders/{created.Id}", created);
            case CreateOrderOutcome.Repeated:
                return TypedResults.Ok(Created(result));
            case CreateOrderOutcome.KeyReused:
                return OrderProblems.IdempotencyKeyReused(result.Id!.Value, result.Number!);
            default:
                return ValidationProblem(result.Errors);
        }
    }

    private static async Task<Results<Ok<OrderVersionResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> EditAsync(
        Guid id, OrderRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken)
    {
        if (CurrentAccount(context.User) is not { } accountId)
        {
            return TypedResults.Unauthorized();
        }

        var result = await dispatcher.SendAsync(
            new EditOrderCommand(accountId, id, OrderContracts.ToInput(request), request?.Version),
            cancellationToken);

        return result.Outcome switch
        {
            EditOrderOutcome.Saved => TypedResults.Ok(new OrderVersionResponse(OrderVersion.Format(result.Version!))),
            EditOrderOutcome.NotFound => OrderProblems.NotFound(),
            EditOrderOutcome.Conflict => OrderProblems.Conflict(),
            EditOrderOutcome.FieldOwnedByShop => OrderProblems.FieldOwnedByShop([.. (result.Fields ?? []).Select(OrderCodes.Of)]),
            EditOrderOutcome.StatusChangeRequiresAction => OrderProblems.StatusChangeRequiresAction(),
            EditOrderOutcome.CustomerAnonymized => OrderProblems.CustomerAnonymized(),
            EditOrderOutcome.SalesReductionLimitExceeded =>
                OrderProblems.ValueBelowSalesReductions(result.MinimumValue!.Value, OrderInput.FormatValue(result.MinimumValue.Value)),
            _ => ValidationProblem(result.Errors),
        };
    }

    private static Task<Results<Ok<OrderVersionResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> CancelAsync(
        Guid id, OrderVersionRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        StatusActionAsync(
            context,
            accountId => dispatcher.SendAsync(new CancelOrderCommand(accountId, id, request?.Version), cancellationToken),
            OrderProblems.AlreadyCancelled);

    private static Task<Results<Ok<OrderVersionResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> RestoreAsync(
        Guid id, OrderVersionRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        StatusActionAsync(
            context,
            accountId => dispatcher.SendAsync(new RestoreOrderCommand(accountId, id, request?.Version), cancellationToken),
            OrderProblems.NotCancelled);

    private static async Task<Results<Ok<OrderVersionResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> StatusActionAsync(
        HttpContext context,
        Func<Guid, Task<OrderStatusActionResult>> send,
        Func<ProblemHttpResult> invalidState)
    {
        if (CurrentAccount(context.User) is not { } accountId)
        {
            return TypedResults.Unauthorized();
        }

        var result = await send(accountId);
        return result.Outcome switch
        {
            OrderStatusActionOutcome.Saved => TypedResults.Ok(new OrderVersionResponse(OrderVersion.Format(result.Version!))),
            OrderStatusActionOutcome.NotFound => OrderProblems.NotFound(),
            OrderStatusActionOutcome.Conflict => OrderProblems.Conflict(),
            OrderStatusActionOutcome.InvalidState => invalidState(),
            _ => ValidationProblem(result.Errors),
        };
    }

    /// <summary>„Anonimizuj dane klienta” (SF-22); dostępność sprawdza handler niezależnie od UI.</summary>
    private static async Task<Results<Ok<OrderVersionResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> AnonymizeAsync(
        Guid id, OrderVersionRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken)
    {
        if (CurrentAccount(context.User) is not { } accountId)
        {
            return TypedResults.Unauthorized();
        }

        var result = await dispatcher.SendAsync(new AnonymizeCustomerCommand(accountId, id, request?.Version), cancellationToken);
        return result.Outcome switch
        {
            AnonymizeCustomerOutcome.Saved => TypedResults.Ok(new OrderVersionResponse(OrderVersion.Format(result.Version!))),
            AnonymizeCustomerOutcome.Disabled => OrderProblems.AnonymizationDisabled(),
            AnonymizeCustomerOutcome.NotFound => OrderProblems.NotFound(),
            AnonymizeCustomerOutcome.Conflict => OrderProblems.Conflict(),
            AnonymizeCustomerOutcome.AlreadyAnonymized => OrderProblems.AlreadyAnonymized(),
            AnonymizeCustomerOutcome.RetentionNotElapsed => OrderProblems.RetentionNotElapsed(result.AvailableFrom!.Value),
            _ => ValidationProblem(result.Errors),
        };
    }

    private static OrderCreatedResponse Created(CreateOrderResult result) =>
        new(result.Id!.Value, result.Number!, OrderVersion.Format(result.Version!));

    internal static ValidationProblem ValidationProblem(FieldErrors? errors) =>
        TypedResults.ValidationProblem(errors?.ToDictionary() ?? new Dictionary<string, string[]>());

    /// <summary>Klucz idempotencji: UUID z nagłówka; brak, pusty albo inny tekst to <c>null</c>.</summary>
    internal static Guid? IdempotencyKey(HttpRequest request) =>
        request.Headers[IdempotencyKeyHeader] is { Count: 1 } values
        && Guid.TryParse(values[0], out var key)
        && key != Guid.Empty
            ? key
            : null;

    internal static Guid? CurrentAccount(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
