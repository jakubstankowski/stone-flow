using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Orders.Infrastructure.Persistence;
using StoneFlow.Shared.Infrastructure.Cqrs;
using StoneFlow.Shared.Infrastructure.Persistence;

namespace StoneFlow.Orders.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOrdersInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<OrdersDbContext>((serviceProvider, options) => options.UseModuleSqlServer<OrdersDbContext>(
            serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString(ModuleDbContextOptions.ConnectionStringName),
            OrdersDbContext.Schema));

        services.AddHandlersFromAssembly(Application.AssemblyReference.Assembly);

        return services;
    }
}
