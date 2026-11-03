using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Integrations.Infrastructure.Persistence;
using StoneFlow.Shared.Infrastructure.Cqrs;
using StoneFlow.Shared.Infrastructure.Persistence;

namespace StoneFlow.Integrations.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIntegrationsInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<IntegrationsDbContext>((serviceProvider, options) => options.UseModuleSqlServer<IntegrationsDbContext>(
            serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString(ModuleDbContextOptions.ConnectionStringName),
            IntegrationsDbContext.Schema));

        services.AddHandlersFromAssembly(Application.AssemblyReference.Assembly);

        return services;
    }
}
