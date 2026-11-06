using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StoneFlow.Integrations.Application.WooCommerce;
using StoneFlow.Integrations.Application.WooCommerce.Backfill;
using StoneFlow.Integrations.Infrastructure.Persistence;
using StoneFlow.Integrations.Infrastructure.WooCommerce;
using StoneFlow.Integrations.Infrastructure.WooCommerce.Rest;
using StoneFlow.Shared.Infrastructure.Cqrs;
using StoneFlow.Shared.Infrastructure.Persistence;
using StoneFlow.Shared.Infrastructure.Scheduling;

namespace StoneFlow.Integrations.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIntegrationsInfrastructure(this IServiceCollection services)
    {
        // Własne połączenie (jak SchedulingDbContext): historia błędów i przebiegi nie dołączają do
        // jednostki pracy zamówień, więc wycofany zapis zamówienia nie cofa zapisu błędu.
        services.AddDbContext<IntegrationsDbContext>((serviceProvider, options) =>
            options.UseModuleSqlServer<IntegrationsDbContext>(
                serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString(ModuleDbContextOptions.ConnectionStringName),
                IntegrationsDbContext.Schema));

        // WooCommerce (SF-12, SF-13): sekret i klucze REST tylko w konfiguracji serwera; brak → wyłączone.
        services.AddSingleton<IValidateOptions<WooCommerceOptions>, WooCommerceOptionsValidator>();
        services.AddOptions<WooCommerceOptions>().BindConfiguration(WooCommerceOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IWooCommerceWebhookSettings, WooCommerceWebhookSettings>();
        services.AddSingleton<IWooCommerceRestSettings, WooCommerceRestSettings>();
        services.AddSingleton<IWooCommerceWebhookEvents, WooCommerceWebhookEvents>();
        services.AddSingleton<IWooCommerceSyncEvents, WooCommerceSyncEvents>();
        services.AddSingleton<IShopOrderSyncRunner, ShopOrderSyncRunner>();

        // Klucze tylko w nagłówku Authorization; bez domyślnych logów HttpClient (adresy żądań).
        services.AddHttpClient<IWooCommerceRestClient, WooCommerceRestClient>((serviceProvider, client) =>
                WooCommerceRestClient.Configure(client, serviceProvider.GetRequiredService<IOptions<WooCommerceOptions>>().Value))
            .RemoveAllLoggers();

        services.AddScoped<IWooCommerceSyncStore, WooCommerceSyncStore>();
        services.AddScoped<IWooCommerceOrderErrors, WooCommerceOrderErrors>();
        services.AddScoped<OrderOutcome>();
        services.AddScoped<WooCommerceBackfill>();

        // Zadanie rejestrowane zawsze (SF-14 widzi stan); bez kluczy REST przebieg nic nie robi.
        services.AddScheduledJob<BackfillJob>(BackfillJob.Definition);
        services.AddSingleton<ManualSyncService>();
        services.AddSingleton<IManualSyncQueue>(serviceProvider => serviceProvider.GetRequiredService<ManualSyncService>());
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<ManualSyncService>());

        services.AddHandlersFromAssembly(Application.AssemblyReference.Assembly);

        return services;
    }
}
