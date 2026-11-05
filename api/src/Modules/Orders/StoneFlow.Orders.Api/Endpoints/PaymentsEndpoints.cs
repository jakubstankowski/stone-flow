using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using StoneFlow.Orders.Application;
using StoneFlow.Orders.Application.AddPayment;
using StoneFlow.Orders.Application.AddRefund;
using StoneFlow.Orders.Application.AddSalesCorrection;
using StoneFlow.Orders.Application.DeletePayment;
using StoneFlow.Orders.Application.DeleteRefund;
using StoneFlow.Orders.Application.DeleteSalesCorrection;
using StoneFlow.Orders.Application.EditPayment;
using StoneFlow.Orders.Application.EditRefund;
using StoneFlow.Orders.Application.EditSalesCorrection;
using StoneFlow.Orders.Application.Payments;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Orders.Api.Endpoints;

/// <summary>
/// Endpointy wpisów zamówienia: <c>/api/orders/{orderId}/payments</c>,
/// <c>/api/orders/{orderId}/refunds</c> i <c>/api/orders/{orderId}/sales-corrections</c>. Bez logiki biznesowej: każde wywołanie przez dyspozytor.
/// Wszystkie wymagają sesji, a zapisy (także <c>DELETE</c>) tokenu XSRF. Wersja wpisu w
/// <c>PUT</c> i <c>DELETE</c> idzie w ciele JSON (<c>{ version }</c>), jak w <c>cancel</c>.
/// </summary>
internal static class PaymentsEndpoints
{
    private static readonly EntryKind PaymentKind = new("paymentId", "wpłatę");
    private static readonly EntryKind RefundKind = new("refundId", "zwrot");
    private static readonly EntryKind SalesCorrectionKind = new("salesCorrectionId", "korektę sprzedaży");

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/orders/{orderId:guid}").WithTags("Orders");

        group.MapPost("/payments", AddPaymentAsync).WithName("Orders.AddPayment");
        group.MapPut("/payments/{entryId:guid}", EditPaymentAsync).WithName("Orders.EditPayment");
        group.MapDelete("/payments/{entryId:guid}", DeletePaymentAsync).WithName("Orders.DeletePayment");
        group.MapPost("/refunds", AddRefundAsync).WithName("Orders.AddRefund");
        group.MapPut("/refunds/{entryId:guid}", EditRefundAsync).WithName("Orders.EditRefund");
        group.MapDelete("/refunds/{entryId:guid}", DeleteRefundAsync).WithName("Orders.DeleteRefund");
        group.MapPost("/sales-corrections", AddSalesCorrectionAsync).WithName("Orders.AddSalesCorrection");
        group.MapPut("/sales-corrections/{entryId:guid}", EditSalesCorrectionAsync).WithName("Orders.EditSalesCorrection");
        group.MapDelete("/sales-corrections/{entryId:guid}", DeleteSalesCorrectionAsync).WithName("Orders.DeleteSalesCorrection");
    }

    private static Task<Results<Created<EntryCreatedResponse>, Ok<EntryCreatedResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> AddPaymentAsync(
        Guid orderId, PaymentRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        AddAsync(
            context,
            orderId,
            PaymentKind,
            (accountId, key) => dispatcher.SendAsync(new AddPaymentCommand(accountId, orderId, key, PaymentContracts.ToInput(request)), cancellationToken));

    private static Task<Results<Ok<OrderVersionResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> EditPaymentAsync(
        Guid orderId, Guid entryId, PaymentRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        EditAsync(
            context,
            accountId => dispatcher.SendAsync(
                new EditPaymentCommand(accountId, orderId, entryId, PaymentContracts.ToInput(request), request?.Version), cancellationToken));

    private static Task<Results<NoContent, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> DeletePaymentAsync(
        Guid orderId, Guid entryId, [FromBody] OrderVersionRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        DeleteAsync(
            context,
            accountId => dispatcher.SendAsync(new DeletePaymentCommand(accountId, orderId, entryId, request?.Version), cancellationToken));

    private static Task<Results<Created<EntryCreatedResponse>, Ok<EntryCreatedResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> AddRefundAsync(
        Guid orderId, RefundRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        AddAsync(
            context,
            orderId,
            RefundKind,
            (accountId, key) => dispatcher.SendAsync(new AddRefundCommand(accountId, orderId, key, PaymentContracts.ToInput(request)), cancellationToken));

    private static Task<Results<Ok<OrderVersionResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> EditRefundAsync(
        Guid orderId, Guid entryId, RefundRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        EditAsync(
            context,
            accountId => dispatcher.SendAsync(
                new EditRefundCommand(accountId, orderId, entryId, PaymentContracts.ToInput(request), request?.Version), cancellationToken));

    private static Task<Results<NoContent, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> DeleteRefundAsync(
        Guid orderId, Guid entryId, [FromBody] OrderVersionRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        DeleteAsync(
            context,
            accountId => dispatcher.SendAsync(new DeleteRefundCommand(accountId, orderId, entryId, request?.Version), cancellationToken));

    private static Task<Results<Created<EntryCreatedResponse>, Ok<EntryCreatedResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> AddSalesCorrectionAsync(
        Guid orderId, SalesCorrectionRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        AddAsync(
            context,
            orderId,
            SalesCorrectionKind,
            (accountId, key) => dispatcher.SendAsync(
                new AddSalesCorrectionCommand(accountId, orderId, key, PaymentContracts.ToInput(request)), cancellationToken));

    private static Task<Results<Ok<OrderVersionResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> EditSalesCorrectionAsync(
        Guid orderId, Guid entryId, SalesCorrectionRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        EditAsync(
            context,
            accountId => dispatcher.SendAsync(
                new EditSalesCorrectionCommand(accountId, orderId, entryId, PaymentContracts.ToInput(request), request?.Version), cancellationToken));

    private static Task<Results<NoContent, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> DeleteSalesCorrectionAsync(
        Guid orderId, Guid entryId, [FromBody] OrderVersionRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        DeleteAsync(
            context,
            accountId => dispatcher.SendAsync(new DeleteSalesCorrectionCommand(accountId, orderId, entryId, request?.Version), cancellationToken));

    private static async Task<Results<Created<EntryCreatedResponse>, Ok<EntryCreatedResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> AddAsync(
        HttpContext context, Guid orderId, EntryKind kind, Func<Guid, Guid, Task<EntryWriteResult>> send)
    {
        if (OrdersEndpoints.CurrentAccount(context.User) is not { } accountId)
        {
            return TypedResults.Unauthorized();
        }

        if (OrdersEndpoints.IdempotencyKey(context.Request) is not { } key)
        {
            return OrderProblems.IdempotencyKeyRequired();
        }

        var result = await send(accountId, key);
        switch (result.Outcome)
        {
            case EntryWriteOutcome.Created:
                // Wpis nie ma własnego odczytu: jest widoczny w szczegółach zamówienia.
                return TypedResults.Created($"/api/orders/{orderId}", Created(result));
            case EntryWriteOutcome.Repeated:
                return TypedResults.Ok(Created(result));
            case EntryWriteOutcome.KeyReused:
                return OrderProblems.EntryIdempotencyKeyReused(kind.IdProperty, result.Id!.Value, kind.Name);
            case EntryWriteOutcome.ValidationFailed:
                return OrdersEndpoints.ValidationProblem(result.Errors);
            default:
                return Problem(result);
        }
    }

    private static async Task<Results<Ok<OrderVersionResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> EditAsync(
        HttpContext context, Func<Guid, Task<EntryWriteResult>> send)
    {
        if (OrdersEndpoints.CurrentAccount(context.User) is not { } accountId)
        {
            return TypedResults.Unauthorized();
        }

        var result = await send(accountId);
        return result.Outcome switch
        {
            EntryWriteOutcome.Saved => TypedResults.Ok(new OrderVersionResponse(OrderVersion.Format(result.Version!))),
            EntryWriteOutcome.ValidationFailed => OrdersEndpoints.ValidationProblem(result.Errors),
            _ => Problem(result),
        };
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> DeleteAsync(
        HttpContext context, Func<Guid, Task<EntryWriteResult>> send)
    {
        if (OrdersEndpoints.CurrentAccount(context.User) is not { } accountId)
        {
            return TypedResults.Unauthorized();
        }

        var result = await send(accountId);
        return result.Outcome switch
        {
            EntryWriteOutcome.Deleted => TypedResults.NoContent(),
            EntryWriteOutcome.ValidationFailed => OrdersEndpoints.ValidationProblem(result.Errors),
            _ => Problem(result),
        };
    }

    private static ProblemHttpResult Problem(EntryWriteResult result) => result.Outcome switch
    {
        EntryWriteOutcome.OrderNotFound => OrderProblems.NotFound(),
        EntryWriteOutcome.EntryNotFound => OrderProblems.EntryNotFound(),
        EntryWriteOutcome.Conflict => OrderProblems.EntryConflict(),
        EntryWriteOutcome.PaidWouldBeNegative => OrderProblems.PaidWouldBeNegative(),
        EntryWriteOutcome.RefundExceedsPaid => OrderProblems.RefundExceedsPaid(result.Available!.Value, OrderInput.FormatValue(result.Available.Value)),
        EntryWriteOutcome.SalesReductionLimitExceeded =>
            OrderProblems.SalesReductionLimitExceeded(result.Available!.Value, OrderInput.FormatValue(result.Available.Value)),
        _ => throw new InvalidOperationException($"Nieoczekiwany wynik zapisu wpisu: {result.Outcome}."),
    };

    private static EntryCreatedResponse Created(EntryWriteResult result) => new(result.Id!.Value, OrderVersion.Format(result.Version!));

    /// <summary>Rodzaj wpisu w odpowiedzi <c>idempotency_key_reused</c>: rozszerzenie z identyfikatorem i nazwa w komunikacie.</summary>
    private sealed record EntryKind(string IdProperty, string Name);
}
