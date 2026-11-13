using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Reporting.Application.Abstractions;
using StoneFlow.Reporting.Infrastructure.Audit;
using StoneFlow.Reporting.Infrastructure.Persistence;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Infrastructure.Cqrs;
using StoneFlow.Shared.Infrastructure.Persistence;

namespace StoneFlow.Reporting.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddReportingInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<ReportingDbContext>((serviceProvider, options) =>
            options.UseModuleSqlServer<ReportingDbContext>(serviceProvider, ReportingDbContext.Schema));

        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IAuditRedaction, AuditRedactor>();
        services.AddScoped<IAuditActivity, AuditActivity>();
        services.AddScoped<IChangeHistoryReader, ChangeHistoryReader>();

        services.AddHandlersFromAssembly(Application.AssemblyReference.Assembly);

        return services;
    }
}
