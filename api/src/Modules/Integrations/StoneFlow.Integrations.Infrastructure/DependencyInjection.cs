using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StoneFlow.Integrations.Application.WooCommerce;
using StoneFlow.Integrations.Infrastructure.Persistence;
using StoneFlow.Integrations.Infrastructure.WooCommerce;
using StoneFlow.Shared.Infrastructure.Cqrs;
using StoneFlow.Shared.Infrastructure.Persistence;

namespace StoneFlow.Integrations.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIntegrationsInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<IntegrationsDbContext>((serviceProvider, options) =>
            options.UseModuleSqlServer<IntegrationsDbContext>(serviceProvider, IntegrationsDbContext.Schema));

        // Webhooki WooCommerce (SF-12): sekret tylko w konfiguracji serwera; brak → integracja wyłączona.
        services.AddSingleton<IValidateOptions<WooCommerceOptions>, WooCommerceOptionsValidator>();
        services.AddOptions<WooCommerceOptions>().BindConfiguration(WooCommerceOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IWooCommerceWebhookSettings, WooCommerceWebhookSettings>();
        services.AddSingleton<IWooCommerceWebhookEvents, WooCommerceWebhookEvents>();
        services.AddSingleton<IShopOrderSyncRunner, ShopOrderSyncRunner>();

        services.AddHandlersFromAssembly(Application.AssemblyReference.Assembly);

        return services;
    }
}
