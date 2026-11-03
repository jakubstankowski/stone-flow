using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Reporting.Infrastructure.Persistence;
using StoneFlow.Shared.Infrastructure.Cqrs;
using StoneFlow.Shared.Infrastructure.Persistence;

namespace StoneFlow.Reporting.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddReportingInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<ReportingDbContext>((serviceProvider, options) => options.UseModuleSqlServer<ReportingDbContext>(
            serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString(ModuleDbContextOptions.ConnectionStringName),
            ReportingDbContext.Schema));

        services.AddHandlersFromAssembly(Application.AssemblyReference.Assembly);

        return services;
    }
}
