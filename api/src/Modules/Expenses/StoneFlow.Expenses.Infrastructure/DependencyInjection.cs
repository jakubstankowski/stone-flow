using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Expenses.Application;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Application.Recurring;
using StoneFlow.Expenses.Infrastructure.Persistence;
using StoneFlow.Expenses.Infrastructure.Recurring;
using StoneFlow.Shared.Application.Costs;
using StoneFlow.Shared.Application.Operations;
using StoneFlow.Shared.Infrastructure.Cqrs;
using StoneFlow.Shared.Infrastructure.Persistence;
using StoneFlow.Shared.Infrastructure.Scheduling;

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

        // Kontrakt z jedną implementacją (SF-19): koszty dla raportu sprzedaży.
        services.AddScoped<ICostSource, ExpenseCostSource>();

        services.AddScoped<IRecurringExpenseStore, RecurringExpenseStore>();
        services.AddScoped<IRecurringExpenseReader, RecurringExpenseReader>();
        services.AddScoped<IRecurringDefinitionLock, RecurringDefinitionLock>();
        services.AddScoped<OccurrenceGenerator>();
        services.AddScoped<RecurringChangeRunner>();
        services.AddScheduledJob<RecurringExpensesJob>(RecurringExpensesJob.Definition);

        services.AddHandlersFromAssembly(Application.AssemblyReference.Assembly);

        return services;
    }
}
