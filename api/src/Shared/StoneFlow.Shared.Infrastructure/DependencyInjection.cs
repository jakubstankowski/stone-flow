using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.LaunchDate;
using StoneFlow.Shared.Infrastructure.Cqrs;
using StoneFlow.Shared.Infrastructure.LaunchDate;

namespace StoneFlow.Shared.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSharedInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IDispatcher, Dispatcher>();

        services.AddSingleton<IValidateOptions<LaunchDateOptions>, LaunchDateOptionsValidator>();
        services.AddOptions<LaunchDateOptions>()
            .BindConfiguration(LaunchDateOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<ILaunchDate, ConfiguredLaunchDate>();

        return services;
    }
}
