using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoneFlow.Integrations.Application.WooCommerce;
using StoneFlow.Shared.Application.ShopOrders;

namespace StoneFlow.Integrations.Infrastructure.WooCommerce;

/// <summary>Sekcja konfiguracji <c>StoneFlow:WooCommerce</c> (sekret tylko na serwerze).</summary>
public sealed class WooCommerceOptions
{
    public const string SectionName = "StoneFlow:WooCommerce";

    public const int MinSecretLength = 16;

    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan MaxRequestTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Sekret webhooków; brak oznacza wyłączoną integrację.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Adres sklepu (<c>https</c>) dla REST v3 (SF-13); brak trójki REST wyłącza uzupełnianie i „Ponów”.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Klucz REST (uprawnienie „Read”); tylko w nagłówku <c>Authorization</c>.</summary>
    public string? ConsumerKey { get; set; }

    /// <summary>Sekret klucza REST; tylko w nagłówku <c>Authorization</c>.</summary>
    public string? ConsumerSecret { get; set; }

    /// <summary>Limit czasu jednego żądania REST.</summary>
    public TimeSpan RequestTimeout { get; set; } = DefaultRequestTimeout;

    /// <summary>Czy podano pełną trójkę REST.</summary>
    public bool RestConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ConsumerKey) && !string.IsNullOrWhiteSpace(ConsumerSecret);
}

public sealed class WooCommerceOptionsValidator : IValidateOptions<WooCommerceOptions>
{
    public ValidateOptionsResult Validate(string? name, WooCommerceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.WebhookSecret) && options.WebhookSecret.Length < WooCommerceOptions.MinSecretLength)
        {
            failures.Add($"'{WooCommerceOptions.SectionName}:WebhookSecret' musi mieć co najmniej {WooCommerceOptions.MinSecretLength} znaków.");
        }

        var given = new[] { options.BaseUrl, options.ConsumerKey, options.ConsumerSecret }.Count(v => !string.IsNullOrWhiteSpace(v));
        if (given is > 0 and < 3)
        {
            failures.Add($"'{WooCommerceOptions.SectionName}': podaj razem BaseUrl, ConsumerKey i ConsumerSecret albo żadnego z nich.");
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl)
            && !(Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(url.Query)))
        {
            failures.Add($"'{WooCommerceOptions.SectionName}:BaseUrl' musi być adresem https bez parametrów.");
        }

        if (options.RequestTimeout <= TimeSpan.Zero || options.RequestTimeout > WooCommerceOptions.MaxRequestTimeout)
        {
            failures.Add($"'{WooCommerceOptions.SectionName}:RequestTimeout' musi mieścić się w zakresie (0, {WooCommerceOptions.MaxRequestTimeout}].");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

internal sealed class WooCommerceWebhookSettings(IOptions<WooCommerceOptions> options) : IWooCommerceWebhookSettings
{
    public string? Secret => string.IsNullOrWhiteSpace(options.Value.WebhookSecret) ? null : options.Value.WebhookSecret;
}

internal sealed class WooCommerceRestSettings(IOptions<WooCommerceOptions> options) : IWooCommerceRestSettings
{
    public bool IsConfigured => options.Value.RestConfigured;
}

/// <summary>Każda próba w nowym zakresie DI; jedno ponowienie po naruszeniu unikalności.</summary>
internal sealed class ShopOrderSyncRunner(IServiceScopeFactory scopeFactory) : IShopOrderSyncRunner
{
    public async Task<ShopOrderSyncResult> ApplyAsync(ShopOrderSnapshot snapshot, CancellationToken cancellationToken)
    {
        var result = await ApplyInNewScopeAsync(snapshot, cancellationToken);
        return result == ShopOrderSyncResult.RetryRequired ? await ApplyInNewScopeAsync(snapshot, cancellationToken) : result;
    }

    private async Task<ShopOrderSyncResult> ApplyInNewScopeAsync(ShopOrderSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IShopOrderSync>().ApplyAsync(snapshot, cancellationToken);
    }
}

internal sealed partial class WooCommerceWebhookEvents(ILogger<WooCommerceWebhookEvents> logger) : IWooCommerceWebhookEvents
{
    public void SignatureRejected(string? deliveryId) => LogSignatureRejected(deliveryId);

    public void Unreadable(string? topic, string? deliveryId, string problem) => LogUnreadable(topic, deliveryId, problem);

    public void IgnoredTopic(string? topic, string? deliveryId) => LogIgnoredTopic(topic, deliveryId);

    public void Applied(string topic, string? deliveryId, string externalId, ShopOrderSyncResult result) =>
        LogApplied(topic, deliveryId, externalId, result);

    public void UnexpectedCurrency(string externalId, string currency) => LogUnexpectedCurrency(externalId, currency);

    public void UnknownStatus(string externalId, string status) => LogUnknownStatus(externalId, status);

    [LoggerMessage(EventId = 7200, Level = LogLevel.Warning, Message = "Webhook WooCommerce: odrzucony podpis, dostawa {DeliveryId}.")]
    private partial void LogSignatureRejected(string? deliveryId);

    [LoggerMessage(EventId = 7201, Level = LogLevel.Warning, Message = "Webhook WooCommerce: nieczytelna treść ({Topic}, dostawa {DeliveryId}): {Problem}.")]
    private partial void LogUnreadable(string? topic, string? deliveryId, string problem);

    [LoggerMessage(EventId = 7202, Level = LogLevel.Information, Message = "Webhook WooCommerce: pominięty temat {Topic}, dostawa {DeliveryId}.")]
    private partial void LogIgnoredTopic(string? topic, string? deliveryId);

    [LoggerMessage(EventId = 7203, Level = LogLevel.Information, Message = "Webhook WooCommerce: {Topic} zamówienia {ExternalId} (dostawa {DeliveryId}) → {Result}.")]
    private partial void LogApplied(string topic, string? deliveryId, string externalId, ShopOrderSyncResult result);

    [LoggerMessage(EventId = 7204, Level = LogLevel.Warning, Message = "Webhook WooCommerce: zamówienie {ExternalId} w walucie {Currency} (oczekiwano PLN).")]
    private partial void LogUnexpectedCurrency(string externalId, string currency);

    [LoggerMessage(EventId = 7205, Level = LogLevel.Information, Message = "Webhook WooCommerce: niestandardowy status {Status} zamówienia {ExternalId}.")]
    private partial void LogUnknownStatus(string externalId, string status);

    // EventId 7206 (błąd zapisu w webhooku) wycofany w SF-13: błąd loguje raz OrderOutcome (7305).
}
