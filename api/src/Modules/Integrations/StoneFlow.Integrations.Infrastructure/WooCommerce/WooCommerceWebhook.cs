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

    /// <summary>Sekret webhooków; brak oznacza wyłączoną integrację.</summary>
    public string? WebhookSecret { get; set; }
}

public sealed class WooCommerceOptionsValidator : IValidateOptions<WooCommerceOptions>
{
    public ValidateOptionsResult Validate(string? name, WooCommerceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return string.IsNullOrWhiteSpace(options.WebhookSecret) || options.WebhookSecret.Length >= WooCommerceOptions.MinSecretLength
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"'{WooCommerceOptions.SectionName}:WebhookSecret' musi mieć co najmniej {WooCommerceOptions.MinSecretLength} znaków.");
    }
}

internal sealed class WooCommerceWebhookSettings(IOptions<WooCommerceOptions> options) : IWooCommerceWebhookSettings
{
    public string? Secret => string.IsNullOrWhiteSpace(options.Value.WebhookSecret) ? null : options.Value.WebhookSecret;
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

    public void Failed(Exception exception, string topic, string? deliveryId, string externalId) =>
        LogFailed(exception, topic, deliveryId, externalId);

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

    [LoggerMessage(EventId = 7206, Level = LogLevel.Error, Message = "Webhook WooCommerce: błąd zapisu {Topic} zamówienia {ExternalId} (dostawa {DeliveryId}).")]
    private partial void LogFailed(Exception exception, string topic, string? deliveryId, string externalId);
}
