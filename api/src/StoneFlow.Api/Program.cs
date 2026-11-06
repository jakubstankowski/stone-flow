using StoneFlow.Access.Api;
using StoneFlow.Access.Infrastructure;
using StoneFlow.Api.Health;
using StoneFlow.Api.Observability;
using StoneFlow.Api.Scheduling;
using StoneFlow.Api.Security;
using StoneFlow.Expenses.Api;
using StoneFlow.Expenses.Infrastructure;
using StoneFlow.Integrations.Api;
using StoneFlow.Integrations.Infrastructure;
using StoneFlow.Orders.Api;
using StoneFlow.Orders.Infrastructure;
using StoneFlow.Reporting.Api;
using StoneFlow.Reporting.Infrastructure;
using StoneFlow.Shared.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

builder.Services.AddStoneFlowProblemDetails(builder.Environment);
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddCheck<SqlServerHealthCheck>("sql");
builder.Services.AddStoneFlowSecurity();
builder.Services.AddStoneFlowHttps();

builder.Services.AddSharedInfrastructure();
builder.Services
    .AddAccessInfrastructure().AddAccessModule()
    .AddOrdersInfrastructure().AddOrdersModule()
    .AddExpensesInfrastructure().AddExpensesModule()
    .AddReportingInfrastructure().AddReportingModule()
    .AddIntegrationsInfrastructure().AddIntegrationsModule();

var app = builder.Build();

// Kolejność ma znaczenie: uwierzytelnianie i autoryzacja przed CSRF, aby wygasła sesja dawała 401.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler(new ExceptionHandlerOptions { StatusCodeSelector = SecuritySetup.SelectStatusCode });
app.UseStatusCodePages();
app.UseStoneFlowHttps();
app.UseRouting();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<CsrfValidationMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapHealthChecks("/health").AllowAnonymous();

app.MapAccessEndpoints();
app.MapOrdersEndpoints();
app.MapExpensesEndpoints();
app.MapReportingEndpoints();
app.MapIntegrationsEndpoints();
app.MapJobsEndpoints();

await app.RunAsync();

public partial class Program;
