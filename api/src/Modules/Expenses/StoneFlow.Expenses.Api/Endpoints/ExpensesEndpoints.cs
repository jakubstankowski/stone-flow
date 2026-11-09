using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using StoneFlow.Expenses.Application;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Application.CreateExpense;
using StoneFlow.Expenses.Application.DeleteExpense;
using StoneFlow.Expenses.Application.EditExpense;
using StoneFlow.Expenses.Application.GetExpense;
using StoneFlow.Expenses.Application.ListExpenses;
using StoneFlow.Expenses.Application.MarkExpensePaid;
using StoneFlow.Expenses.Application.UnmarkExpensePaid;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Expenses.Api.Endpoints;

/// <summary>
/// Endpointy <c>/api/expenses</c>. Bez logiki biznesowej: każde wywołanie przez dyspozytor, wynik
/// mapowany na odpowiedź HTTP. Wszystkie wymagają sesji (polityka domyślna hosta), a zapisy tokenu
/// XSRF (globalny middleware). Wersja w <c>PUT</c>, akcjach i <c>DELETE</c> idzie w ciele JSON.
/// </summary>
internal static class ExpensesEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/expenses").WithTags("Expenses");

        group.MapGet(string.Empty, ListAsync).WithName("Expenses.List");
        group.MapGet("/{id:guid}", GetAsync).WithName("Expenses.Get");
        group.MapPost(string.Empty, CreateAsync).WithName("Expenses.Create");
        group.MapPut("/{id:guid}", EditAsync).WithName("Expenses.Edit");
        group.MapPost("/{id:guid}/mark-paid", MarkPaidAsync).WithName("Expenses.MarkPaid");
        group.MapPost("/{id:guid}/unmark-paid", UnmarkPaidAsync).WithName("Expenses.UnmarkPaid");
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("Expenses.Delete");
    }

    private static async Task<Results<Ok<ExpenseListPageResponse>, ValidationProblem>> ListAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] string? page = null,
        [FromQuery] string? pageSize = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? direction = null,
        [FromQuery] string? costDateFrom = null,
        [FromQuery] string? costDateTo = null,
        [FromQuery] string? paidOnFrom = null,
        [FromQuery] string? paidOnTo = null,
        [FromQuery] string[]? category = null,
        [FromQuery] string? state = null,
        [FromQuery] string? sign = null,
        [FromQuery] string? search = null)
    {
        var result = await dispatcher.QueryAsync(
            new ListExpensesQuery(page, pageSize, sort, direction, costDateFrom, costDateTo, paidOnFrom, paidOnTo, category, state, sign, search),
            cancellationToken);

        return result.Page is { } list
            ? TypedResults.Ok(ExpenseContracts.ToResponse(list))
            : ValidationProblem(result.Errors);
    }

    private static async Task<Results<Ok<ExpenseDetailsResponse>, ProblemHttpResult>> GetAsync(
        Guid id, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var expense = await dispatcher.QueryAsync(new GetExpenseQuery(id), cancellationToken);
        return expense is null ? ExpenseProblems.NotFound() : TypedResults.Ok(ExpenseContracts.ToResponse(expense));
    }

    private static async Task<Results<Created<ExpenseCreatedResponse>, Ok<ExpenseCreatedResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> CreateAsync(
        ExpenseRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken)
    {
        if (EndpointSupport.CurrentAccount(context.User) is not { } accountId)
        {
            return TypedResults.Unauthorized();
        }

        if (EndpointSupport.IdempotencyKey(context.Request) is not { } key)
        {
            return ExpenseProblems.IdempotencyKeyRequired();
        }

        var result = await dispatcher.SendAsync(new CreateExpenseCommand(accountId, key, ExpenseContracts.ToInput(request)), cancellationToken);
        switch (result.Outcome)
        {
            case CreateExpenseOutcome.Created:
                var created = new ExpenseCreatedResponse(result.Id!.Value, ExpenseVersion.Format(result.Version!));
                return TypedResults.Created($"/api/expenses/{created.Id}", created);
            case CreateExpenseOutcome.Repeated:
                return TypedResults.Ok(new ExpenseCreatedResponse(result.Id!.Value, ExpenseVersion.Format(result.Version!)));
            case CreateExpenseOutcome.KeyReused:
                return ExpenseProblems.IdempotencyKeyReused(result.Id!.Value);
            default:
                return ValidationProblem(result.Errors);
        }
    }

    private static Task<Results<Ok<ExpenseVersionResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> EditAsync(
        Guid id, ExpenseRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        ChangeAsync(
            context,
            accountId => dispatcher.SendAsync(new EditExpenseCommand(accountId, id, ExpenseContracts.ToInput(request), request?.Version), cancellationToken));

    private static Task<Results<Ok<ExpenseVersionResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> MarkPaidAsync(
        Guid id, MarkExpensePaidRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        ChangeAsync(
            context,
            accountId => dispatcher.SendAsync(
                new MarkExpensePaidCommand(accountId, id, request?.PaidOn, request?.PaymentMethod, request?.Version), cancellationToken));

    private static Task<Results<Ok<ExpenseVersionResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> UnmarkPaidAsync(
        Guid id, ExpenseVersionRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        ChangeAsync(
            context,
            accountId => dispatcher.SendAsync(new UnmarkExpensePaidCommand(accountId, id, request?.Version), cancellationToken));

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> DeleteAsync(
        Guid id, [FromBody] ExpenseVersionRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken)
    {
        if (EndpointSupport.CurrentAccount(context.User) is not { } accountId)
        {
            return TypedResults.Unauthorized();
        }

        var result = await dispatcher.SendAsync(new DeleteExpenseCommand(accountId, id, request?.Version), cancellationToken);
        return result.Outcome switch
        {
            ExpenseChangeOutcome.Saved => TypedResults.NoContent(),
            ExpenseChangeOutcome.NotFound => ExpenseProblems.NotFound(),
            ExpenseChangeOutcome.Conflict => ExpenseProblems.Conflict(),
            _ => ValidationProblem(result.Errors),
        };
    }

    private static async Task<Results<Ok<ExpenseVersionResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> ChangeAsync(
        HttpContext context, Func<Guid, Task<ExpenseChangeResult>> send)
    {
        if (EndpointSupport.CurrentAccount(context.User) is not { } accountId)
        {
            return TypedResults.Unauthorized();
        }

        var result = await send(accountId);
        return result.Outcome switch
        {
            ExpenseChangeOutcome.Saved => TypedResults.Ok(new ExpenseVersionResponse(ExpenseVersion.Format(result.Version!))),
            ExpenseChangeOutcome.NotFound => ExpenseProblems.NotFound(),
            ExpenseChangeOutcome.Conflict => ExpenseProblems.Conflict(),
            ExpenseChangeOutcome.AlreadyPaid => ExpenseProblems.AlreadyPaid(),
            ExpenseChangeOutcome.NotPaid => ExpenseProblems.NotPaid(),
            _ => ValidationProblem(result.Errors),
        };
    }

    private static ValidationProblem ValidationProblem(FieldErrors? errors) =>
        TypedResults.ValidationProblem(errors?.ToDictionary() ?? new Dictionary<string, string[]>());
}
