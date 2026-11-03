using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Expenses.Infrastructure.Persistence;
using StoneFlow.Shared.Infrastructure.Cqrs;
using StoneFlow.Shared.Infrastructure.Persistence;

namespace StoneFlow.Expenses.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddExpensesInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<ExpensesDbContext>((serviceProvider, options) => options.UseModuleSqlServer<ExpensesDbContext>(
            serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString(ModuleDbContextOptions.ConnectionStringName),
            ExpensesDbContext.Schema));

        services.AddHandlersFromAssembly(Application.AssemblyReference.Assembly);

        return services;
    }
}
