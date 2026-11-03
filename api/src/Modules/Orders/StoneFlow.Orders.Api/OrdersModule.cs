using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace StoneFlow.Orders.Api;

/// <summary>Rejestracja modułu w hoście. Moduł nie ma jeszcze endpointów.</summary>
public static class OrdersModule
{
    public static IServiceCollection AddOrdersModule(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
}
