using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Expenses.Api.Endpoints;

namespace StoneFlow.Expenses.Api;

/// <summary>Rejestracja modułu w hoście: endpointy <c>/api/expenses</c> (wydatki i ich zapłata).</summary>
public static class ExpensesModule
{
    public static IServiceCollection AddExpensesModule(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapExpensesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ExpensesEndpoints.Map(endpoints);
        return endpoints;
    }
}
