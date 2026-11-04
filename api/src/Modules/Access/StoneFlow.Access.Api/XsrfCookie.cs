using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace StoneFlow.Access.Api;

/// <summary>
/// Token CSRF dla frontendu: ciasteczko <c>XSRF-TOKEN</c> czytelne dla JS (Angular przepisuje je
/// do nagłówka <c>X-XSRF-TOKEN</c>), związane z tożsamością bieżącego żądania.
/// </summary>
public static class XsrfCookie
{
    public const string CookieName = "XSRF-TOKEN";
    public const string HeaderName = "X-XSRF-TOKEN";

    public static void Issue(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        var development = context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment();
        var tokens = antiforgery.GetAndStoreTokens(context);

        context.Response.Cookies.Append(CookieName, tokens.RequestToken ?? string.Empty, new CookieOptions
        {
            HttpOnly = false,
            Secure = !development || context.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true,
        });
    }
}
