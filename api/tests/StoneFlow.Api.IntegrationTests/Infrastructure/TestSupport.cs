using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Access.Application.Abstractions;
using StoneFlow.Access.Infrastructure.Persistence;
using StoneFlow.Expenses.Infrastructure.Persistence;
using StoneFlow.Integrations.Infrastructure.Persistence;
using StoneFlow.Orders.Infrastructure.Persistence;
using StoneFlow.Reporting.Infrastructure.Persistence;

namespace StoneFlow.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Wyłącznie w testach: nagłówek <c>X-Test-Account</c> ustawia uwierzytelnionego użytkownika (testy
/// ścieżek chronionych bez bazy), a <c>X-Test-Remote-Ip</c> adres klienta (limity żądań).
/// </summary>
internal sealed class TestRequestStartupFilter : IStartupFilter
{
    public const string AccountHeader = "X-Test-Account";
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue(RemoteIpHeader, out var ip) && IPAddress.TryParse(ip, out var address))
            {
                context.Connection.RemoteIpAddress = address;
            }

            if (context.Request.Headers.TryGetValue(AccountHeader, out var account))
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, account.ToString())],
                    authenticationType: "Test"));
            }

            return nextMiddleware(context);
        });
        next(app);
    };
}

/// <summary>Przechwytuje wiadomości zamiast wysyłki SMTP.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    public IReadOnlyCollection<EmailMessage> Messages => _messages;

    public Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _messages.Enqueue(message);
        return Task.FromResult(EmailDeliveryResult.Sent);
    }

    /// <summary>Konto i token z linku w ostatniej wiadomości (fragment <c>#konto=…&amp;token=…</c>).</summary>
    public ResetLink LastLink()
    {
        var body = _messages.Last().TextBody;
        var line = body.Split('\n').Single(l => l.Contains("/reset-hasla/nowe#", StringComparison.Ordinal)).Trim();
        var link = new Uri(line);
        var parameters = link.Fragment.TrimStart('#').Split('&')
            .Select(part => part.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]), StringComparer.Ordinal);
        return new ResetLink(link, parameters["konto"], parameters["token"]);
    }
}

public sealed record ResetLink(Uri Uri, string AccountId, string Token);

internal static class TestDatabase
{
    /// <summary>Stosuje migracje wszystkich modułów na bazie z konfiguracji fabryki.</summary>
    public static async Task MigrateAsync(StoneFlowApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        DbContext[] contexts =
        [
            scope.ServiceProvider.GetRequiredService<AccessDbContext>(),
            scope.ServiceProvider.GetRequiredService<OrdersDbContext>(),
            scope.ServiceProvider.GetRequiredService<ExpensesDbContext>(),
            scope.ServiceProvider.GetRequiredService<ReportingDbContext>(),
            scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>(),
        ];

        foreach (var context in contexts)
        {
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }
    }

    public static async Task<T> QueryAsync<T>(StoneFlowApiFactory factory, Func<AccessDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AccessDbContext>());
    }
}
