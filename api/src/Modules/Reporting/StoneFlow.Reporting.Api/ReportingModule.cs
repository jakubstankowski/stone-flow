using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Reporting.Api.Endpoints;

namespace StoneFlow.Reporting.Api;

/// <summary>
/// Rejestracja modułu w hoście: endpointy <c>/api/operations</c> (lista operacji pieniężnych, SF-17)
/// i <c>/api/change-history</c> (historia zmian z audytu, SF-18).
/// </summary>
public static class ReportingModule
{
    public static IServiceCollection AddReportingModule(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        OperationsEndpoints.Map(endpoints);
        ChangeHistoryEndpoints.Map(endpoints);
        return endpoints;
    }
}
