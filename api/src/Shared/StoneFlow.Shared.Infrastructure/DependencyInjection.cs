using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.LaunchDate;
using StoneFlow.Shared.Application.Persistence;
using StoneFlow.Shared.Infrastructure.Cqrs;
using StoneFlow.Shared.Infrastructure.LaunchDate;
using StoneFlow.Shared.Infrastructure.Persistence;
using StoneFlow.Shared.Infrastructure.Scheduling;

namespace StoneFlow.Shared.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSharedInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IDispatcher, Dispatcher>();
        services.TryAddSingleton(TimeProvider.System);

        // Wspólne połączenie i transakcja kontekstów modułów finansowych (SF-7).
        services.AddScoped<ModuleConnection>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddSingleton<IValidateOptions<LaunchDateOptions>, LaunchDateOptionsValidator>();
        services.AddOptions<LaunchDateOptions>()
            .BindConfiguration(LaunchDateOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<ILaunchDate, ConfiguredLaunchDate>();

        // Harmonogram zadań automatycznych (SF-11) i jego zapytanie stanu.
        services.AddScheduling();
        services.AddHandlersFromAssembly(Application.AssemblyReference.Assembly);

        return services;
    }
}
