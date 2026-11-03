using System.Diagnostics;

namespace StoneFlow.Api.Observability;

/// <summary>
/// Nadaje każdemu żądaniu identyfikator korelacji równy W3C TraceId bieżącej aktywności.
/// Identyfikator trafia do zakresu logów i do nagłówka odpowiedzi <c>X-Correlation-ID</c>.
/// Przychodzący nagłówek <c>X-Correlation-ID</c> jest ignorowany; źródłem jest wyłącznie <c>traceparent</c>.
/// </summary>
internal sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    private const string ItemKey = "StoneFlow.CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        using var fallbackActivity = Activity.Current is null
            ? new Activity("StoneFlow.Request").SetIdFormat(ActivityIdFormat.W3C).Start()
            : null;

        var correlationId = (Activity.Current ?? fallbackActivity)!.TraceId.ToHexString();
        context.Items[ItemKey] = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }

    public static string? Get(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) ? value as string : null;
}
