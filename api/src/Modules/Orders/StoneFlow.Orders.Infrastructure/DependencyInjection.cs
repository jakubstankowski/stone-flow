using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StoneFlow.Orders.Application;
using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Application.AnonymizeCustomer;
using StoneFlow.Orders.Application.Payments;
using StoneFlow.Orders.Application.ShopSync;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Orders.Infrastructure.Options;
using StoneFlow.Orders.Infrastructure.Persistence;
using StoneFlow.Shared.Application.Operations;
using StoneFlow.Shared.Application.Sales;
using StoneFlow.Shared.Application.ShopOrders;
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
        services.AddScoped<IOrderLock, OrderLock>();
        services.AddScoped<IShopOrderStore, ShopOrderStore>();
        services.AddScoped<IShopOrderSync, ShopOrderSync>();
        services.AddScoped<IShopOrderLookup, ShopOrderLookup>();
        services.AddScoped<IOrderEntryStore<Payment>, OrderEntryStore<Payment>>();
        services.AddScoped<IOrderEntryStore<Refund>, OrderEntryStore<Refund>>();
        services.AddScoped<IOrderEntryStore<SalesCorrection>, OrderEntryStore<SalesCorrection>>();
        services.AddScoped<OrderEntryWriter<Payment>>();
        services.AddScoped<OrderEntryWriter<Refund>>();
        services.AddScoped<OrderEntryWriter<SalesCorrection>>();

        // Anonimizacja danych klienta (SF-22): okres retencji z konfiguracji, bez wartości domyślnej (D-09).
        services.AddSingleton<IValidateOptions<AnonymizationOptions>, AnonymizationOptionsValidator>();
        services.AddOptions<AnonymizationOptions>()
            .BindConfiguration(AnonymizationOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IAnonymizationSettings, ConfiguredAnonymizationSettings>();
        services.AddScoped<IOrderAnonymizationStore, OrderAnonymizationStore>();
        services.AddScoped<AnonymizationRetention>();

        // Kontrakt z wieloma implementacjami (Orders, Expenses): zwykłe AddScoped, aby rejestracje się sumowały.
        services.AddScoped<ICashOperationSource, OrderCashOperationSource>();

        // Kontrakt z jedną implementacją (SF-19): składniki sprzedaży dla raportu.
        services.AddScoped<ISalesComponentSource, OrderSalesComponentSource>();

        services.AddHandlersFromAssembly(Application.AssemblyReference.Assembly);

        return services;
    }
}
