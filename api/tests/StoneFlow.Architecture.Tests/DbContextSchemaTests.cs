using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Access.Infrastructure;
using StoneFlow.Access.Infrastructure.Persistence;
using StoneFlow.Expenses.Infrastructure;
using StoneFlow.Expenses.Infrastructure.Persistence;
using StoneFlow.Integrations.Infrastructure;
using StoneFlow.Integrations.Infrastructure.Persistence;
using StoneFlow.Orders.Infrastructure;
using StoneFlow.Orders.Infrastructure.Persistence;
using StoneFlow.Reporting.Infrastructure;
using StoneFlow.Reporting.Infrastructure.Persistence;

namespace StoneFlow.Architecture.Tests;

/// <summary>Niezmiennik 7: każdy DbContext mapuje wyłącznie schemat swojego modułu.</summary>
public sealed class DbContextSchemaTests
{
    [Theory]
    [MemberData(nameof(Modules.All), MemberType = typeof(Modules))]
    public void Context_uses_only_its_schema(string module)
    {
        var (register, contextType, schema) = Module(module);
        using var provider = BuildProvider(register);
        using var scope = provider.CreateScope();
        var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);

        var model = context.GetService<IDesignTimeModel>().Model;
        Assert.Equal(schema, model.GetDefaultSchema());
        Assert.All(model.GetEntityTypes(), entity => Assert.Equal(schema, entity.GetSchema()));

        var relational = RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>());
        Assert.Equal(schema, relational.MigrationsHistoryTableSchema);
        Assert.Equal(contextType.Assembly.GetName().Name, relational.MigrationsAssembly);
    }

    [Theory]
    [MemberData(nameof(Modules.All), MemberType = typeof(Modules))]
    public void Schema_is_module_name_in_lower_case(string module) =>
        Assert.Equal(module.ToLowerInvariant(), Module(module).Schema);

    private static ServiceProvider BuildProvider(Action<IServiceCollection> register)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:StoneFlow"] = "Server=localhost;Database=StoneFlowArchitectureTests;TrustServerCertificate=True",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        register(services);
        return services.BuildServiceProvider();
    }

    private static (Action<IServiceCollection> Register, Type ContextType, string Schema) Module(string module) => module switch
    {
        "Access" => (s => s.AddAccessInfrastructure(), typeof(AccessDbContext), AccessDbContext.Schema),
        "Orders" => (s => s.AddOrdersInfrastructure(), typeof(OrdersDbContext), OrdersDbContext.Schema),
        "Expenses" => (s => s.AddExpensesInfrastructure(), typeof(ExpensesDbContext), ExpensesDbContext.Schema),
        "Reporting" => (s => s.AddReportingInfrastructure(), typeof(ReportingDbContext), ReportingDbContext.Schema),
        "Integrations" => (s => s.AddIntegrationsInfrastructure(), typeof(IntegrationsDbContext), IntegrationsDbContext.Schema),
        _ => throw new ArgumentOutOfRangeException(nameof(module), module, "Nieznany moduł."),
    };
}
