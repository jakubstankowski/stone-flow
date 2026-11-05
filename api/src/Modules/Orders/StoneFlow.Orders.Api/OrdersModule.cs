using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Orders.Api.Endpoints;

namespace StoneFlow.Orders.Api;

/// <summary>Rejestracja modułu w hoście: endpointy <c>/api/orders</c> (zamówienia, wpłaty i zwroty).</summary>
public static class OrdersModule
{
    public static IServiceCollection AddOrdersModule(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        OrdersEndpoints.Map(endpoints);
        PaymentsEndpoints.Map(endpoints);
        return endpoints;
    }
}
