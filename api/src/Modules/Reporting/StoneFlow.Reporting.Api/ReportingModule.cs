using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace StoneFlow.Reporting.Api;

/// <summary>Rejestracja modułu w hoście. Moduł nie ma jeszcze endpointów.</summary>
public static class ReportingModule
{
    public static IServiceCollection AddReportingModule(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
}
