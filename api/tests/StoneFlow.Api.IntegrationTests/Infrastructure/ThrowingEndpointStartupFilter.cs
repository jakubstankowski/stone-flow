using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace StoneFlow.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Dodaje na końcu potoku testową ścieżkę rzucającą wyjątek, aby sprawdzić obsługę błędów hosta.
/// Istnieje wyłącznie w testach.
/// </summary>
internal sealed class ThrowingEndpointStartupFilter : IStartupFilter
{
    public const string Path = "/__test/throw";

    public const string ExceptionMessage = "Wyjątek testowy z danymi wewnętrznymi";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        app.Use((context, nextMiddleware) => context.Request.Path == Path
            ? throw new InvalidOperationException(ExceptionMessage)
            : nextMiddleware(context));
    };
}
