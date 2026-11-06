using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoneFlow.Shared.Application.Scheduling;
using StoneFlow.Shared.Infrastructure.Persistence;

namespace StoneFlow.Shared.Infrastructure.Scheduling;

public static class SchedulingRegistration
{
    /// <summary>Harmonogram zadań (SF-11); wołane z <c>AddSharedInfrastructure</c>.</summary>
    internal static IServiceCollection AddScheduling(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<SchedulingOptions>, SchedulingOptionsValidator>();
        services.AddOptions<SchedulingOptions>()
            .BindConfiguration(SchedulingOptions.SectionName)
            .ValidateOnStart();

        services.AddDbContext<SchedulingDbContext>((serviceProvider, options) =>
            options.UseModuleSqlServer<SchedulingDbContext>(
                serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString(ModuleDbContextOptions.ConnectionStringName),
                SchedulingDbContext.Schema));

        services.AddSingleton<JobRegistry>();
        services.AddScoped<JobLeaseStore>();
        services.AddSingleton<JobExecutor>();
        services.AddSingleton(serviceProvider => new JobScheduler(
            serviceProvider.GetRequiredService<JobRegistry>(),
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredService<JobExecutor>(),
            serviceProvider.GetRequiredService<TimeProvider>(),
            serviceProvider.GetRequiredService<ILogger<JobScheduler>>()));
        services.AddSingleton<IJobRunner>(serviceProvider => serviceProvider.GetRequiredService<JobScheduler>());
        services.AddScoped<IJobStatusReader, JobStatusReader>();
        services.AddHostedService<JobSchedulerService>();

        return services;
    }

    /// <summary>
    /// Rejestruje zadanie harmonogramu: definicję (klucz, harmonogram, maksymalny czas) i implementację
    /// jako usługę scoped, rozwiązywaną w nowym zakresie każdego przebiegu.
    /// </summary>
    public static IServiceCollection AddScheduledJob<TJob>(this IServiceCollection services, JobDefinition definition)
        where TJob : class, IScheduledJob
    {
        ArgumentNullException.ThrowIfNull(definition);

        services.AddSingleton(definition);
        services.AddKeyedScoped<IScheduledJob, TJob>(definition.Key);
        return services;
    }
}
