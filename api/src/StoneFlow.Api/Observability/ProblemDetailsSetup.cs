namespace StoneFlow.Api.Observability;

internal static class ProblemDetailsSetup
{
    /// <summary>
    /// Problem Details dla wszystkich błędów, z <c>traceId</c> równym identyfikatorowi korelacji.
    /// Poza środowiskiem Development odpowiedź nie zawiera szczegółów wyjątku.
    /// </summary>
    public static IServiceCollection AddStoneFlowProblemDetails(this IServiceCollection services, IHostEnvironment environment) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var correlationId = CorrelationIdMiddleware.Get(context.HttpContext);
            if (correlationId is not null)
            {
                context.ProblemDetails.Extensions["traceId"] = correlationId;
            }

            if (!environment.IsDevelopment())
            {
                context.ProblemDetails.Detail = null;
                context.ProblemDetails.Extensions.Remove("exception");
            }
        });
}
