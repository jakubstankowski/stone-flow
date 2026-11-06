using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace StoneFlow.Integrations.Api;

/// <summary>
/// Rejestracja modułu w hoście: webhook zamówień WooCommerce (SF-12) i endpointy synchronizacji (SF-13).
/// </summary>
public static class IntegrationsModule
{
    public static IServiceCollection AddIntegrationsModule(this IServiceCollection services)
    {
        WooCommerceWebhookEndpoint.AddRateLimit(services);
        return services;
    }

    public static IEndpointRouteBuilder MapIntegrationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        WooCommerceWebhookEndpoint.Map(endpoints);
        WooCommerceEndpoints.Map(endpoints);
        return endpoints;
    }
}
