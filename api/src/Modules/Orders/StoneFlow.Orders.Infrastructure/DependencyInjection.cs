using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Orders.Application;
using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Infrastructure.Persistence;
using StoneFlow.Shared.Infrastructure.Cqrs;
using StoneFlow.Shared.Infrastructure.Persistence;

namespace StoneFlow.Orders.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOrdersInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<OrdersDbContext>((serviceProvider, options) =>
            options.UseModuleSqlServer<OrdersDbContext>(serviceProvider, OrdersDbContext.Schema));

        services.AddScoped<IOrderStore, OrderStore>();
        services.AddScoped<IOrderNumberAllocator, OrderNumberAllocator>();
        services.AddScoped<IOrderReader, OrderReader>();
        services.AddScoped<OrderStatusAction>();

        services.AddHandlersFromAssembly(Application.AssemblyReference.Assembly);

        return services;
    }
}
