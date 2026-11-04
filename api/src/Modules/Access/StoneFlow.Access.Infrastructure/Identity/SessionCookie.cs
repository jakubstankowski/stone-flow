using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Access.Application.Abstractions;
using StoneFlow.Access.Domain.Sessions;
using StoneFlow.Access.Infrastructure.Persistence;

namespace StoneFlow.Access.Infrastructure.Identity;

public static class AccessClaimTypes
{
    /// <summary>Czas zalogowania (sekundy Unix) — podstawa limitu 12 godzin sesji.</summary>
    public const string AuthTime = "sf:auth_time";
}

/// <summary>Zdarzenia ciasteczka sesji.</summary>
internal static class SessionCookieEvents
{
    public static CookieAuthenticationEvents Create() => new()
    {
        OnValidatePrincipal = ValidatePrincipalAsync,
        OnRedirectToLogin = context => Status(context, StatusCodes.Status401Unauthorized),
        OnRedirectToAccessDenied = context => Status(context, StatusCodes.Status403Forbidden),
    };

    /// <summary>
    /// Kolejno: limit 12 h od zalogowania, znacznik bezpieczeństwa (przy każdym żądaniu — reset
    /// hasła i wylogowanie działają od razu), przesunięcie terminu bezczynności. Brak lub
    /// nieczytelny czas zalogowania odrzuca sesję.
    /// </summary>
    /// <remarks>
    /// Znacznik jest sprawdzany przez <c>SignInManager.ValidateSecurityStampAsync</c>, a nie przez
    /// <c>SecurityStampValidator</c>: ten przy odrzuceniu wylogowuje także schematy dwuskładnikowe
    /// i zewnętrzne, których moduł nie rejestruje. Principal pozostaje bez zmian, więc zachowuje
    /// czas zalogowania.
    /// </remarks>
    private static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var services = context.HttpContext.RequestServices;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var authenticatedAt = ReadAuthTime(context.Principal);
        var valid = authenticatedAt is not null
            && SessionPolicy.IsWithinAbsoluteLifetime(authenticatedAt.Value, now)
            && await services.GetRequiredService<SignInManager<AccessUser>>().ValidateSecurityStampAsync(context.Principal) is not null;

        if (!valid)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(context.Scheme.Name);
            return;
        }

        context.ShouldRenew = true;
    }

    internal static DateTimeOffset? ReadAuthTime(ClaimsPrincipal? principal) =>
        principal?.FindFirst(AccessClaimTypes.AuthTime)?.Value is { } value
        && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    private static Task Status(RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        // API nie przekierowuje na stronę logowania — zwraca status (treść dopisuje UseStatusCodePages).
        context.Response.StatusCode = statusCode;
        return Task.CompletedTask;
    }
}

internal sealed class IdentitySessionManager(
    SignInManager<AccessUser> signInManager,
    UserManager<AccessUser> userManager,
    IHttpContextAccessor httpContextAccessor) : ISessionManager
{
    public async Task SignInAsync(Guid accountId, DateTimeOffset authenticatedAt, CancellationToken cancellationToken)
    {
        var httpContext = HttpContext();
        var user = await userManager.FindByIdAsync(accountId.ToString("D"))
            ?? throw new InvalidOperationException("Konto nie istnieje.");

        var principal = await signInManager.CreateUserPrincipalAsync(user);
        var identity = principal.Identities.First();
        identity.AddClaim(new Claim(
            AccessClaimTypes.AuthTime,
            authenticatedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            ClaimValueTypes.Integer64));

        await httpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal, new AuthenticationProperties { IsPersistent = false });

        // Nowa tożsamość jest od razu widoczna w tym żądaniu (np. dla nowego tokenu CSRF).
        httpContext.User = principal;
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        var httpContext = HttpContext();
        await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
    }

    private HttpContext HttpContext() =>
        httpContextAccessor.HttpContext ?? throw new InvalidOperationException("Sesja wymaga bieżącego żądania HTTP.");
}
