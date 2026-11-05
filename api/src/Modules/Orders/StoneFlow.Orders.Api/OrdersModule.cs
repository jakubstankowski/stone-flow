using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Orders.Api.Endpoints;

namespace StoneFlow.Orders.Api;

/// <summary>Rejestracja modułu w hoście: endpointy <c>/api/orders</c>.</summary>
public static class OrdersModule
{
    public static IServiceCollection AddOrdersModule(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        OrdersEndpoints.Map(endpoints);
        return endpoints;
    }
}
