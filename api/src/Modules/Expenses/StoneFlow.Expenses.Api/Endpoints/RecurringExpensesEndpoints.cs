using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using StoneFlow.Expenses.Application;
using StoneFlow.Expenses.Application.CreateRecurringExpense;
using StoneFlow.Expenses.Application.DeleteRecurringExpense;
using StoneFlow.Expenses.Application.EditRecurringExpense;
using StoneFlow.Expenses.Application.GetRecurringExpense;
using StoneFlow.Expenses.Application.ListRecurringExpenses;
using StoneFlow.Expenses.Application.PauseRecurringExpense;
using StoneFlow.Expenses.Application.PreviewRecurringExpense;
using StoneFlow.Expenses.Application.Recurring;
using StoneFlow.Expenses.Application.ResumeRecurringExpense;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Expenses.Api.Endpoints;

/// <summary>
/// Endpointy <c>/api/recurring-expenses</c> (SF-16). Bez logiki biznesowej: każde wywołanie przez
/// dyspozytor, wynik mapowany na odpowiedź HTTP. Wszystkie wymagają sesji (polityka domyślna hosta),
/// a zapisy tokenu XSRF (globalny middleware). Wersja w <c>PUT</c>, akcjach i <c>DELETE</c> idzie
/// w ciele JSON.
/// </summary>
internal static class RecurringExpensesEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/recurring-expenses").WithTags("Expenses");

        group.MapGet(string.Empty, ListAsync).WithName("Expenses.ListRecurring");
        group.MapGet("/preview", PreviewAsync).WithName("Expenses.PreviewRecurring");
        group.MapGet("/{id:guid}", GetAsync).WithName("Expenses.GetRecurring");
        group.MapPost(string.Empty, CreateAsync).WithName("Expenses.CreateRecurring");
        group.MapPut("/{id:guid}", EditAsync).WithName("Expenses.EditRecurring");
        group.MapPost("/{id:guid}/pause", PauseAsync).WithName("Expenses.PauseRecurring");
        group.MapPost("/{id:guid}/resume", ResumeAsync).WithName("Expenses.ResumeRecurring");
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("Expenses.DeleteRecurring");
    }

    private static async Task<Results<Ok<RecurringExpenseListPageResponse>, ValidationProblem>> ListAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] string? page = null,
        [FromQuery] string? pageSize = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? direction = null,
        [FromQuery] string? state = null,
        [FromQuery] string? search = null)
    {
        var result = await dispatcher.QueryAsync(new ListRecurringExpensesQuery(page, pageSize, sort, direction, state, search), cancellationToken);
        return result.Page is { } list
            ? TypedResults.Ok(RecurringExpenseContracts.ToResponse(list))
            : ExpensesEndpoints.ValidationProblem(result.Errors);
    }

    private static async Task<Results<Ok<RecurringExpenseDetailsResponse>, ProblemHttpResult>> GetAsync(
        Guid id, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var details = await dispatcher.QueryAsync(new GetRecurringExpenseQuery(id), cancellationToken);
        return details is null ? ExpenseProblems.RecurringNotFound() : TypedResults.Ok(RecurringExpenseContracts.ToResponse(details));
    }

    private static async Task<Results<Ok<RecurringExpensePreviewResponse>, ValidationProblem, ProblemHttpResult>> PreviewAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] string? dayOfMonth = null,
        [FromQuery] string? startsOn = null,
        [FromQuery] string? endsOn = null,
        [FromQuery] string? definitionId = null)
    {
        var result = await dispatcher.QueryAsync(new PreviewRecurringExpenseQuery(dayOfMonth, startsOn, endsOn, definitionId), cancellationToken);
        return result.Outcome switch
        {
            PreviewRecurringExpenseOutcome.Previewed => TypedResults.Ok(RecurringExpenseContracts.ToResponse(result.Preview!)),
            PreviewRecurringExpenseOutcome.NotFound => ExpenseProblems.RecurringNotFound(),
            PreviewRecurringExpenseOutcome.PausedDayChange => ExpenseProblems.RecurringPaused(),
            _ => ExpensesEndpoints.ValidationProblem(result.Errors),
        };
    }

    private static async Task<Results<Created<RecurringExpenseCreatedResponse>, Ok<RecurringExpenseCreatedResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> CreateAsync(
        RecurringExpenseRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken)
    {
        if (ExpensesEndpoints.CurrentAccount(context.User) is not { } accountId)
        {
            return TypedResults.Unauthorized();
        }

        if (ExpensesEndpoints.IdempotencyKey(context.Request) is not { } key)
        {
            return ExpenseProblems.IdempotencyKeyRequired();
        }

        var result = await dispatcher.SendAsync(
            new CreateRecurringExpenseCommand(accountId, key, RecurringExpenseContracts.ToInput(request)), cancellationToken);
        switch (result.Outcome)
        {
            case CreateRecurringExpenseOutcome.Created:
                var created = new RecurringExpenseCreatedResponse(result.Id!.Value, ExpenseVersion.Format(result.Version!), result.CreatedOccurrences);
                return TypedResults.Created($"/api/recurring-expenses/{created.Id}", created);
            case CreateRecurringExpenseOutcome.Repeated:
                return TypedResults.Ok(new RecurringExpenseCreatedResponse(result.Id!.Value, ExpenseVersion.Format(result.Version!), result.CreatedOccurrences));
            case CreateRecurringExpenseOutcome.KeyReused:
                return ExpenseProblems.RecurringIdempotencyKeyReused(result.Id!.Value);
            case CreateRecurringExpenseOutcome.Deleted:
                return ExpenseProblems.RecurringDeleted();
            default:
                return ExpensesEndpoints.ValidationProblem(result.Errors);
        }
    }

    private static Task<Results<Ok<RecurringExpenseChangedResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> EditAsync(
        Guid id, RecurringExpenseRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        ChangeAsync(
            context,
            accountId => dispatcher.SendAsync(
                new EditRecurringExpenseCommand(accountId, id, RecurringExpenseContracts.ToInput(request), request?.Version), cancellationToken));

    private static Task<Results<Ok<RecurringExpenseChangedResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> PauseAsync(
        Guid id, RecurringExpenseVersionRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        ChangeAsync(context, accountId => dispatcher.SendAsync(new PauseRecurringExpenseCommand(accountId, id, request?.Version), cancellationToken));

    private static Task<Results<Ok<RecurringExpenseChangedResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> ResumeAsync(
        Guid id, RecurringExpenseVersionRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        ChangeAsync(context, accountId => dispatcher.SendAsync(new ResumeRecurringExpenseCommand(accountId, id, request?.Version), cancellationToken));

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> DeleteAsync(
        Guid id, [FromBody] RecurringExpenseVersionRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken)
    {
        if (ExpensesEndpoints.CurrentAccount(context.User) is not { } accountId)
        {
            return TypedResults.Unauthorized();
        }

        var result = await dispatcher.SendAsync(new DeleteRecurringExpenseCommand(accountId, id, request?.Version), cancellationToken);
        return result.Outcome switch
        {
            RecurringChangeOutcome.Saved => TypedResults.NoContent(),
            RecurringChangeOutcome.ValidationFailed => ExpensesEndpoints.ValidationProblem(result.Errors),
            _ => Problem(result.Outcome),
        };
    }

    private static async Task<Results<Ok<RecurringExpenseChangedResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> ChangeAsync(
        HttpContext context, Func<Guid, Task<RecurringChangeResult>> send)
    {
        if (ExpensesEndpoints.CurrentAccount(context.User) is not { } accountId)
        {
            return TypedResults.Unauthorized();
        }

        var result = await send(accountId);
        return result.Outcome switch
        {
            RecurringChangeOutcome.Saved => TypedResults.Ok(new RecurringExpenseChangedResponse(ExpenseVersion.Format(result.Version!), result.CreatedOccurrences)),
            RecurringChangeOutcome.ValidationFailed => ExpensesEndpoints.ValidationProblem(result.Errors),
            _ => Problem(result.Outcome),
        };
    }

    private static ProblemHttpResult Problem(RecurringChangeOutcome outcome) => outcome switch
    {
        RecurringChangeOutcome.NotFound => ExpenseProblems.RecurringNotFound(),
        RecurringChangeOutcome.Conflict => ExpenseProblems.RecurringConflict(),
        RecurringChangeOutcome.AlreadyPaused => ExpenseProblems.RecurringAlreadyPaused(),
        RecurringChangeOutcome.NotPaused => ExpenseProblems.RecurringNotPaused(),
        RecurringChangeOutcome.Ended => ExpenseProblems.RecurringEnded(),
        RecurringChangeOutcome.PausedDayChange => ExpenseProblems.RecurringPaused(),
        RecurringChangeOutcome.Busy => ExpenseProblems.RecurringBusy(),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };
}
