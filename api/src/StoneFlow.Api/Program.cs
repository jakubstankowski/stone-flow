using StoneFlow.Api.Health;
using StoneFlow.Api.Observability;
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

builder.Services.AddSharedInfrastructure();
builder.Services
    .AddOrdersInfrastructure().AddOrdersModule()
    .AddExpensesInfrastructure().AddExpensesModule()
    .AddReportingInfrastructure().AddReportingModule()
    .AddIntegrationsInfrastructure().AddIntegrationsModule();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health").AllowAnonymous();

app.MapOrdersEndpoints();
app.MapExpensesEndpoints();
app.MapReportingEndpoints();
app.MapIntegrationsEndpoints();

await app.RunAsync();

public partial class Program;
