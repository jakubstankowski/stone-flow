using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace StoneFlow.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Host API do testów. Domyślnie środowisko Production z poprawną datą uruchomienia i bez bazy danych.
/// </summary>
public sealed class StoneFlowApiFactory : WebApplicationFactory<Program>
{
    public const string UnreachableConnectionString =
        "Server=tcp:127.0.0.1,1;Database=StoneFlow;User Id=sa;Password=unused;TrustServerCertificate=True;Connect Timeout=1";

    private readonly Dictionary<string, string?> _settings = new()
    {
        ["StoneFlow:LaunchDate"] = "2026-11-03",
    };

    public string Environment { get; init; } = "Production";

    public StoneFlowApiFactory With(string key, string? value)
    {
        _settings[key] = value;
        return this;
    }

    public StoneFlowApiFactory Without(string key)
    {
        _settings.Remove(key);
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(_settings));
        builder.ConfigureLogging(logging => logging.AddFakeLogging());
        builder.ConfigureServices(services => services.AddTransient<IStartupFilter, ThrowingEndpointStartupFilter>());
    }
}
