using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using StoneFlow.Expenses.Application;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Application.CreateOwnerWithdrawal;
using StoneFlow.Expenses.Application.DeleteOwnerWithdrawal;
using StoneFlow.Expenses.Application.EditOwnerWithdrawal;
using StoneFlow.Expenses.Application.GetOwnerWithdrawal;
using StoneFlow.Expenses.Application.ListOwnerWithdrawalPeople;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Expenses.Api.Endpoints;

/// <summary>
/// Endpointy <c>/api/owner-withdrawals</c> (SF-17). Bez logiki biznesowej: każde wywołanie przez
/// dyspozytor, wynik mapowany na odpowiedź HTTP. Wszystkie wymagają sesji (polityka domyślna
/// hosta), a zapisy tokenu XSRF (globalny middleware). Wersja w <c>PUT</c> i <c>DELETE</c> idzie w ciele JSON.
/// </summary>
internal static class OwnerWithdrawalsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/owner-withdrawals").WithTags("OwnerWithdrawals");

        group.MapGet("/people", PeopleAsync).WithName("Expenses.ListOwnerWithdrawalPeople");
        group.MapGet("/{id:guid}", GetAsync).WithName("Expenses.GetOwnerWithdrawal");
        group.MapPost(string.Empty, CreateAsync).WithName("Expenses.CreateOwnerWithdrawal");
        group.MapPut("/{id:guid}", EditAsync).WithName("Expenses.EditOwnerWithdrawal");
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("Expenses.DeleteOwnerWithdrawal");
    }

    private static async Task<Ok<OwnerWithdrawalPeopleResponse>> PeopleAsync(IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var people = await dispatcher.QueryAsync(new ListOwnerWithdrawalPeopleQuery(), cancellationToken);
        return TypedResults.Ok(new OwnerWithdrawalPeopleResponse(people));
    }

    private static async Task<Results<Ok<OwnerWithdrawalDetailsResponse>, ProblemHttpResult>> GetAsync(
        Guid id, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var withdrawal = await dispatcher.QueryAsync(new GetOwnerWithdrawalQuery(id), cancellationToken);
        return withdrawal is null ? OwnerWithdrawalProblems.NotFound() : TypedResults.Ok(OwnerWithdrawalContracts.ToResponse(withdrawal));
    }

    private static async Task<Results<Created<OwnerWithdrawalCreatedResponse>, Ok<OwnerWithdrawalCreatedResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> CreateAsync(
        OwnerWithdrawalRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken)
    {
        if (EndpointSupport.CurrentAccount(context.User) is not { } accountId)
        {
            return TypedResults.Unauthorized();
        }

        if (EndpointSupport.IdempotencyKey(context.Request) is not { } key)
        {
            return OwnerWithdrawalProblems.IdempotencyKeyRequired();
        }

        var result = await dispatcher.SendAsync(
            new CreateOwnerWithdrawalCommand(accountId, key, OwnerWithdrawalContracts.ToInput(request)), cancellationToken);
        switch (result.Outcome)
        {
            case CreateOwnerWithdrawalOutcome.Created:
                var created = new OwnerWithdrawalCreatedResponse(result.Id!.Value, ExpenseVersion.Format(result.Version!));
                return TypedResults.Created($"/api/owner-withdrawals/{created.Id}", created);
            case CreateOwnerWithdrawalOutcome.Repeated:
                return TypedResults.Ok(new OwnerWithdrawalCreatedResponse(result.Id!.Value, ExpenseVersion.Format(result.Version!)));
            case CreateOwnerWithdrawalOutcome.KeyReused:
                return OwnerWithdrawalProblems.IdempotencyKeyReused(result.Id!.Value);
            default:
                return ValidationProblem(result.Errors);
        }
    }

    private static async Task<Results<Ok<OwnerWithdrawalVersionResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> EditAsync(
        Guid id, OwnerWithdrawalRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken)
    {
        if (EndpointSupport.CurrentAccount(context.User) is not { } accountId)
        {
            return TypedResults.Unauthorized();
        }

        var result = await dispatcher.SendAsync(
            new EditOwnerWithdrawalCommand(accountId, id, OwnerWithdrawalContracts.ToInput(request), request?.Version), cancellationToken);
        return result.Outcome switch
        {
            OwnerWithdrawalChangeOutcome.Saved => TypedResults.Ok(new OwnerWithdrawalVersionResponse(ExpenseVersion.Format(result.Version!))),
            OwnerWithdrawalChangeOutcome.NotFound => OwnerWithdrawalProblems.NotFound(),
            OwnerWithdrawalChangeOutcome.Conflict => OwnerWithdrawalProblems.Conflict(),
            _ => ValidationProblem(result.Errors),
        };
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> DeleteAsync(
        Guid id, [FromBody] OwnerWithdrawalVersionRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken)
    {
        if (EndpointSupport.CurrentAccount(context.User) is not { } accountId)
        {
            return TypedResults.Unauthorized();
        }

        var result = await dispatcher.SendAsync(new DeleteOwnerWithdrawalCommand(accountId, id, request?.Version), cancellationToken);
        return result.Outcome switch
        {
            OwnerWithdrawalChangeOutcome.Saved => TypedResults.NoContent(),
            OwnerWithdrawalChangeOutcome.NotFound => OwnerWithdrawalProblems.NotFound(),
            OwnerWithdrawalChangeOutcome.Conflict => OwnerWithdrawalProblems.Conflict(),
            _ => ValidationProblem(result.Errors),
        };
    }

    private static ValidationProblem ValidationProblem(FieldErrors? errors) =>
        TypedResults.ValidationProblem(errors?.ToDictionary() ?? new Dictionary<string, string[]>());
}
