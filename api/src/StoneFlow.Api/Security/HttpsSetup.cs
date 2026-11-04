namespace StoneFlow.Api.Security;

/// <summary>
/// HTTPS poza Development: HSTS (rok, bez subdomen i preload — domyślny adres dostawcy) i trwałe
/// przekierowanie HTTP → HTTPS na standardowy port. TLS kończy serwer hostingu (IIS), a Vercel
/// przekazuje <c>/api/*</c> na adres HTTPS API. Lokalnie (Development) API działa na HTTP.
/// </summary>
internal static class HttpsSetup
{
    public static IServiceCollection AddStoneFlowHttps(this IServiceCollection services)
    {
        services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = false;
            options.Preload = false;
        });
        services.AddHttpsRedirection(options =>
        {
            options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
            options.HttpsPort = 443;
        });
        return services;
    }

    public static WebApplication UseStoneFlowHttps(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        return app;
    }
}
