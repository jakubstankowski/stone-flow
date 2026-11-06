using System.Text;
using System.Text.RegularExpressions;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.ShopOrders;

namespace StoneFlow.Integrations.Application.WooCommerce;

/// <summary>Wynik przyjęcia dostawy webhooka (mapowany na status HTTP).</summary>
public enum WebhookOutcome
{
    /// <summary><c>200</c>: przetworzona, pominięta albo nieczytelna (bez zmian).</summary>
    Accepted,

    /// <summary><c>401</c>: brak lub zły podpis.</summary>
    Unauthorized,

    /// <summary><c>404</c>: integracja wyłączona (brak sekretu).</summary>
    Disabled,

    /// <summary><c>503</c>: błąd zapisu; ponowienie dostawy jest bezpieczne.</summary>
    Unavailable,
}

/// <summary>Dostawa webhooka: surowa treść i nagłówki WooCommerce.</summary>
public sealed record ReceiveWooCommerceWebhookCommand(byte[] Body, string? Signature, string? Topic, string? DeliveryId)
    : ICommand<WebhookOutcome>;

/// <summary>Sekret webhooka z konfiguracji serwera; <c>null</c>, gdy integracja jest wyłączona.</summary>
public interface IWooCommerceWebhookSettings
{
    string? Secret { get; }
}

/// <summary>
/// Zastosowanie dostawy w nowym zakresie DI (nowa jednostka pracy) z jednym ponowieniem przy
/// <see cref="ShopOrderSyncResult.RetryRequired"/>.
/// </summary>
public interface IShopOrderSyncRunner
{
    Task<ShopOrderSyncResult> ApplyAsync(ShopOrderSnapshot snapshot, CancellationToken cancellationToken);
}

/// <summary>Zdarzenia webhooka w logach: bez treści, bez podpisu i bez danych klienta.</summary>
public interface IWooCommerceWebhookEvents
{
    void SignatureRejected(string? deliveryId);

    void Unreadable(string? topic, string? deliveryId, string problem);

    void IgnoredTopic(string? topic, string? deliveryId);

    void Applied(string topic, string? deliveryId, string externalId, ShopOrderSyncResult result);

    void UnexpectedCurrency(string externalId, string currency);

    void UnknownStatus(string externalId, string status);

    void Failed(Exception exception, string topic, string? deliveryId, string externalId);
}

public sealed partial class ReceiveWooCommerceWebhookHandler(
    IWooCommerceWebhookSettings settings,
    IShopOrderSyncRunner sync,
    IWooCommerceWebhookEvents events) : ICommandHandler<ReceiveWooCommerceWebhookCommand, WebhookOutcome>
{
    // Kopia listy z Orders.Domain.ShopStatuses tylko do diagnostyki w logu: moduł nie odwołuje się
    // do domeny innego modułu (niezmiennik 7); reguły statusów pozostają wyłącznie w Orders.
    private static readonly string[] StandardStatuses =
        ["pending", "processing", "on-hold", "completed", "cancelled", "refunded", "failed", "trash", "checkout-draft"];

    public async Task<WebhookOutcome> HandleAsync(ReceiveWooCommerceWebhookCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (settings.Secret is not { } secret)
        {
            return WebhookOutcome.Disabled;
        }

        var deliveryId = Clip(command.DeliveryId);
        var topicHeader = Clip(command.Topic);

        // Ping przy tworzeniu webhooka niczego nie zmienia, więc nie wymaga podpisu.
        if (topicHeader is null && IsPing(command.Body))
        {
            return WebhookOutcome.Accepted;
        }

        if (!WebhookSignature.IsValid(command.Body, command.Signature, secret))
        {
            events.SignatureRejected(deliveryId);
            return WebhookOutcome.Unauthorized;
        }

        if (Topic(topicHeader) is not { } topic)
        {
            events.IgnoredTopic(topicHeader, deliveryId);
            return WebhookOutcome.Accepted;
        }

        var read = WooCommerceOrderPayload.Read(topic, command.Body);
        if (read.Snapshot is not { } snapshot)
        {
            events.Unreadable(topicHeader, deliveryId, read.Problem ?? "nieczytelna treść");
            return WebhookOutcome.Accepted;
        }

        if (read.Currency is { } currency && !string.Equals(currency, "PLN", StringComparison.OrdinalIgnoreCase))
        {
            events.UnexpectedCurrency(snapshot.ExternalId, currency);
        }

        if (snapshot.Status is { } status && !StandardStatuses.Contains(status, StringComparer.Ordinal))
        {
            events.UnknownStatus(snapshot.ExternalId, Clip(status)!);
        }

        try
        {
            var result = await sync.ApplyAsync(snapshot, cancellationToken);
            events.Applied(topicHeader!, deliveryId, snapshot.ExternalId, result);
            return result == ShopOrderSyncResult.RetryRequired ? WebhookOutcome.Unavailable : WebhookOutcome.Accepted;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            events.Failed(exception, topicHeader!, deliveryId, snapshot.ExternalId);
            return WebhookOutcome.Unavailable;
        }
    }

    private static ShopOrderTopic? Topic(string? header) => header switch
    {
        "order.created" => ShopOrderTopic.Created,
        "order.updated" => ShopOrderTopic.Updated,
        "order.restored" => ShopOrderTopic.Restored,
        "order.deleted" => ShopOrderTopic.Deleted,
        _ => null,
    };

    private static bool IsPing(byte[] body) => body.Length <= 64 && PingBody().IsMatch(Encoding.ASCII.GetString(body));

    /// <summary>Nagłówek do logu: przycięty, najwyżej 64 znaki.</summary>
    private static string? Clip(string? text)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length <= 64 ? trimmed : trimmed[..64];
    }

    [GeneratedRegex(@"^webhook_id=\d{1,20}$")]
    private static partial Regex PingBody();
}
