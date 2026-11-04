using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using StoneFlow.Access.Application.Abstractions;

namespace StoneFlow.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Host API do testów. Domyślnie środowisko Production z poprawną konfiguracją wdrożenia, bez bazy
/// danych, z przechwytywaniem wiadomości e-mail i klientem HTTPS (ciasteczka <c>__Host-</c>).
/// </summary>
public sealed class StoneFlowApiFactory : WebApplicationFactory<Program>
{
    public const string UnreachableConnectionString =
        "Server=tcp:127.0.0.1,1;Database=StoneFlow;User Id=sa;Password=unused;TrustServerCertificate=True;Connect Timeout=1";

    public const string OwnerEmail = "owner@example.com";

    public const string AppBaseUrl = "https://app.example.com";

    private readonly Dictionary<string, string?> _settings = new()
    {
        ["StoneFlow:LaunchDate"] = "2026-11-03",
        ["StoneFlow:Owner:Email"] = OwnerEmail,
        ["StoneFlow:App:BaseUrl"] = AppBaseUrl,
        ["StoneFlow:Email:Mode"] = "Smtp",
        ["StoneFlow:Email:Smtp:Host"] = "localhost",
        ["StoneFlow:Email:Smtp:Port"] = "2525",
        ["StoneFlow:Email:Smtp:FromAddress"] = "noreply@example.com",
    };

    public StoneFlowApiFactory()
    {
        ClientOptions.BaseAddress = new Uri("https://localhost");
    }

    public string Environment { get; init; } = "Production";

    /// <summary>Zegar aplikacji (sesja, tokeny); <c>null</c> oznacza zegar systemowy.</summary>
    public FakeTimeProvider? Time { get; init; }

    /// <summary>Prawdziwa wysyłka SMTP zamiast przechwytywania (test błędu transportu).</summary>
    public bool UseRealEmailSender { get; init; }

    public CapturingEmailSender Emails { get; } = new();

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

    public StoneFlowApiFactory WithDatabase(string connectionString) => With("ConnectionStrings:StoneFlow", connectionString);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(_settings));
        builder.ConfigureLogging(logging =>
        {
            logging.Services.AddSingleton(Logs);
            logging.AddProvider(new FakeLoggerProvider(Logs));
        });
        builder.ConfigureServices(services =>
        {
            services.AddTransient<IStartupFilter, TestRequestStartupFilter>();
            services.AddTransient<IStartupFilter, ThrowingEndpointStartupFilter>();
            if (!UseRealEmailSender)
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(Emails);
            }

            if (Time is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Time);
            }
        });
    }
}
