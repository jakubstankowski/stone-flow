using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Expenses.Application;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Infrastructure.Persistence;
using StoneFlow.Shared.Application.Operations;
using StoneFlow.Shared.Infrastructure.Cqrs;
using StoneFlow.Shared.Infrastructure.Persistence;

namespace StoneFlow.Expenses.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddExpensesInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<ExpensesDbContext>((serviceProvider, options) =>
            options.UseModuleSqlServer<ExpensesDbContext>(serviceProvider, ExpensesDbContext.Schema));

        services.AddScoped<IExpenseStore, ExpenseStore>();
        services.AddScoped<IExpenseReader, ExpenseReader>();
        services.AddScoped<ExpenseChangeRunner>();
        services.AddScoped<IOwnerWithdrawalStore, OwnerWithdrawalStore>();
        services.AddScoped<IOwnerWithdrawalReader, OwnerWithdrawalReader>();

        // Kontrakt z wieloma implementacjami (Orders, Expenses): zwykłe AddScoped, aby rejestracje się sumowały.
        services.AddScoped<ICashOperationSource, ExpenseCashOperationSource>();

        services.AddHandlersFromAssembly(Application.AssemblyReference.Assembly);

        return services;
    }
}
