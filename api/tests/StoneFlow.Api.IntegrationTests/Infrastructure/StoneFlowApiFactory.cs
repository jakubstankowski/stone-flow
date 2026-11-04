using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

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

    /// <summary>
    /// Logi hosta zbierane przez kolektor należący do fabryki: przeżywają zwolnienie hosta,
    /// więc pozwalają odczytać błąd startu także wtedy, gdy host zdążył się już zamknąć.
    /// </summary>
    public FakeLogCollector Logs { get; } = new();

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
        builder.ConfigureLogging(logging =>
        {
            logging.Services.AddSingleton(Logs);
            logging.AddProvider(new FakeLoggerProvider(Logs));
        });
        builder.ConfigureServices(services => services.AddTransient<IStartupFilter, ThrowingEndpointStartupFilter>());
    }
}
