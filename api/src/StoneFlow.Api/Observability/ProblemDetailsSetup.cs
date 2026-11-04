namespace StoneFlow.Api.Observability;

internal static class ProblemDetailsSetup
{
    /// <summary>
    /// Problem Details dla wszystkich błędów, z <c>traceId</c> równym identyfikatorowi korelacji.
    /// Poza środowiskiem Development odpowiedź nieobsłużonego wyjątku nie zawiera szczegółów;
    /// komunikaty błędów ustawione celowo przez endpointy (<c>detail</c>) pozostają.
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
                context.ProblemDetails.Extensions.Remove("exception");
                if (context.Exception is not null)
                {
                    context.ProblemDetails.Detail = null;
                }
            }
        });
}
