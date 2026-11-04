using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using StoneFlow.Access.Application.Login;
using StoneFlow.Access.Application.PasswordReset;
using StoneFlow.Access.Application.Session;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Access.Api.Endpoints;

public sealed record LoginRequest(string? Email, string? Password);

public sealed record PasswordResetRequest(string? Email);

public sealed record PasswordResetConfirmRequest(string? AccountId, string? Token, string? NewPassword);

public sealed record SessionResponse(string Email);

/// <summary>
/// Endpointy <c>/api/auth</c>. Bez logiki biznesowej: każde wywołanie przez dyspozytor, wynik
/// mapowany na odpowiedź HTTP. Wyjątki od wymogu sesji są oznaczone <c>AllowAnonymous</c>;
/// CSRF sprawdza globalny middleware hosta dla wszystkich metod zmieniających stan.
/// </summary>
internal static class AuthEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth").WithTags("Auth");

        group.MapGet("/csrf", IssueCsrf).WithName("Access.Csrf").AllowAnonymous();
        group.MapPost("/login", LoginAsync).WithName("Access.Login").AllowAnonymous().RequireRateLimiting(AccessModule.LoginRateLimit);
        group.MapPost("/logout", LogoutAsync).WithName("Access.Logout");
        group.MapGet("/session", GetSessionAsync).WithName("Access.Session");
        group.MapPost("/password-reset/request", RequestResetAsync)
            .WithName("Access.PasswordResetRequest").AllowAnonymous().RequireRateLimiting(AccessModule.ResetRateLimit);
        group.MapPost("/password-reset/confirm", ConfirmResetAsync).WithName("Access.PasswordResetConfirm").AllowAnonymous();
    }

    private static NoContent IssueCsrf(HttpContext context)
    {
        XsrfCookie.Issue(context);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> LoginAsync(
        LoginRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new LoginCommand(request?.Email, request?.Password), cancellationToken);
        switch (result.Outcome)
        {
            case LoginOutcome.Succeeded:
                XsrfCookie.Issue(context);
                return TypedResults.NoContent();
            case LoginOutcome.ValidationFailed:
                return TypedResults.ValidationProblem(result.Errors?.ToDictionary() ?? new Dictionary<string, string[]>());
            default:
                return AuthProblems.InvalidCredentials();
        }
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult>> LogoutAsync(
        IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken)
    {
        if (CurrentAccount(context.User) is not { } accountId)
        {
            return TypedResults.Unauthorized();
        }

        await dispatcher.SendAsync(new LogoutCommand(accountId), cancellationToken);
        XsrfCookie.Issue(context);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<SessionResponse>, UnauthorizedHttpResult>> GetSessionAsync(
        IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken)
    {
        if (CurrentAccount(context.User) is not { } accountId)
        {
            return TypedResults.Unauthorized();
        }

        var session = await dispatcher.QueryAsync(new GetSessionQuery(accountId), cancellationToken);
        return session is null ? TypedResults.Unauthorized() : TypedResults.Ok(new SessionResponse(session.Email));
    }

    private static async Task<Results<Accepted, ValidationProblem>> RequestResetAsync(
        PasswordResetRequest? request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new RequestPasswordResetCommand(request?.Email), cancellationToken);
        return result.Accepted
            ? TypedResults.Accepted((string?)null)
            : TypedResults.ValidationProblem(result.Errors?.ToDictionary() ?? new Dictionary<string, string[]>());
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> ConfirmResetAsync(
        PasswordResetConfirmRequest? request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(
            new ConfirmPasswordResetCommand(request?.AccountId, request?.Token, request?.NewPassword),
            cancellationToken);

        return result.Outcome switch
        {
            ConfirmPasswordResetOutcome.Succeeded => TypedResults.NoContent(),
            ConfirmPasswordResetOutcome.ValidationFailed =>
                TypedResults.ValidationProblem(result.Errors?.ToDictionary() ?? new Dictionary<string, string[]>()),
            _ => AuthProblems.ResetLinkInvalid(),
        };
    }

    private static Guid? CurrentAccount(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
