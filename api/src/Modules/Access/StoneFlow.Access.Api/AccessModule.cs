using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StoneFlow.Access.Api.Endpoints;

namespace StoneFlow.Access.Api;

/// <summary>Rejestracja modułu Access w hoście: CSRF, limity żądań i endpointy <c>/api/auth</c>.</summary>
public static class AccessModule
{
    public const string LoginRateLimit = "auth-login";
    public const string ResetRateLimit = "auth-reset";

    public const string AntiforgeryCookieName = "__Host-sf_af";
    public const string DevelopmentAntiforgeryCookieName = "sf_af";

    public static IServiceCollection AddAccessModule(this IServiceCollection services)
    {
        services.AddAntiforgery();
        services.AddOptions<AntiforgeryOptions>()
            .Configure<IHostEnvironment>((options, environment) =>
            {
                var development = environment.IsDevelopment();
                options.HeaderName = XsrfCookie.HeaderName;
                options.Cookie.Name = development ? DevelopmentAntiforgeryCookieName : AntiforgeryCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.Path = "/";
            });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, _) => await AuthProblems.TooManyRequests().ExecuteAsync(context.HttpContext);
            options.AddPolicy(LoginRateLimit, context => FixedWindowByIp(context, permitLimit: 10, TimeSpan.FromMinutes(1)));
            options.AddPolicy(ResetRateLimit, context => FixedWindowByIp(context, permitLimit: 3, TimeSpan.FromMinutes(15)));
        });

        return services;
    }

    public static IEndpointRouteBuilder MapAccessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        AuthEndpoints.Map(endpoints);
        return endpoints;
    }

    /// <summary>
    /// Partycja po adresie IP połączenia. Za proxy (SF-4) wymaga zaufanych nagłówków przekazania,
    /// inaczej limit stanie się wspólny dla wszystkich użytkowników.
    /// </summary>
    private static RateLimitPartition<string> FixedWindowByIp(HttpContext context, int permitLimit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = window, QueueLimit = 0 });
}
