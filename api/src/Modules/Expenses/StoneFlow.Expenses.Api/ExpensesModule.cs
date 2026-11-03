using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace StoneFlow.Expenses.Api;

/// <summary>Rejestracja modułu w hoście. Moduł nie ma jeszcze endpointów.</summary>
public static class ExpensesModule
{
    public static IServiceCollection AddExpensesModule(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapExpensesEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
}
