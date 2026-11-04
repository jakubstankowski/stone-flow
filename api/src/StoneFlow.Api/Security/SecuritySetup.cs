using System.Data.Common;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using StoneFlow.Access.Api;

namespace StoneFlow.Api.Security;

internal static class SecuritySetup
{
    /// <summary>
    /// DOST-01: domyślnie każdy endpoint (także odczyt i ścieżka bez endpointu) wymaga sesji;
    /// wyjątki są jawnie oznaczone <c>AllowAnonymous</c>. CORS nie dopuszcza żadnego originu —
    /// frontend woła API pod tym samym originem (proxy <c>/api</c>).
    /// </summary>
    public static IServiceCollection AddStoneFlowSecurity(this IServiceCollection services)
    {
        var requireSession = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(requireSession)
            .SetFallbackPolicy(requireSession);

        services.AddCors(options => options.AddDefaultPolicy(policy => policy.SetIsOriginAllowed(_ => false)));
        return services;
    }

    /// <summary>Niedostępna baza (wyjątek bazy w łańcuchu przyczyn) → 503, pozostałe błędy → 500.</summary>
    public static int SelectStatusCode(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException)
            {
                return StatusCodes.Status503ServiceUnavailable;
            }
        }

        return StatusCodes.Status500InternalServerError;
    }
}

/// <summary>
/// CSRF dla wszystkich żądań zmieniających stan pod <c>/api</c> (POST, PUT, PATCH, DELETE), także
/// dla endpointów anonimowych (logowanie, reset). Działa po autoryzacji, więc wygasła sesja daje 401,
/// a nie błąd CSRF.
/// </summary>
internal sealed class CsrfValidationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (RequiresValidation(context.Request) && !await antiforgery.IsRequestValidAsync(context))
        {
            await AuthProblems.CsrfInvalid().ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    private static bool RequiresValidation(HttpRequest request) =>
        request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        && (HttpMethods.IsPost(request.Method)
            || HttpMethods.IsPut(request.Method)
            || HttpMethods.IsPatch(request.Method)
            || HttpMethods.IsDelete(request.Method));
}
