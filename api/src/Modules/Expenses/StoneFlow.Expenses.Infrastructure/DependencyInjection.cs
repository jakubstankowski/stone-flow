using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Expenses.Application;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Application.Recurring;
using StoneFlow.Expenses.Infrastructure.Persistence;
using StoneFlow.Expenses.Infrastructure.Recurring;
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
