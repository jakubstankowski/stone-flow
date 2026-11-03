using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace StoneFlow.Integrations.Api;

/// <summary>Rejestracja modułu w hoście. Moduł nie ma jeszcze endpointów.</summary>
public static class IntegrationsModule
{
    public static IServiceCollection AddIntegrationsModule(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapIntegrationsEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
}
