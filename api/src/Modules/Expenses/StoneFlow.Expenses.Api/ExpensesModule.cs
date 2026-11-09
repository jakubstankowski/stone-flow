using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Expenses.Api.Endpoints;

namespace StoneFlow.Expenses.Api;

/// <summary>
/// Rejestracja modułu w hoście: endpointy <c>/api/expenses</c> (wydatki i ich zapłata),
/// <c>/api/recurring-expenses</c> (wydatki cykliczne) i <c>/api/owner-withdrawals</c> (wypłaty właścicieli).
/// </summary>
public static class ExpensesModule
{
    public static IServiceCollection AddExpensesModule(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapExpensesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ExpensesEndpoints.Map(endpoints);
        OwnerWithdrawalsEndpoints.Map(endpoints);
        RecurringExpensesEndpoints.Map(endpoints);
        return endpoints;
    }
}
