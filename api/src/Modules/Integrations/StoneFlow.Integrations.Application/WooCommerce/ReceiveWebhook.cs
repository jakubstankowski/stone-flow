using System.Text;
using System.Text.RegularExpressions;
using StoneFlow.Integrations.Domain.WooCommerce;
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
}

/// <summary>
/// Przyjęcie dostawy webhooka (SF-12). Od SF-13 nieczytelna treść z ID i błąd zapisu trafiają do
/// historii błędów, a udany zapis zamyka otwarty błąd zamówienia; kody odpowiedzi bez zmian.
/// </summary>
public sealed partial class ReceiveWooCommerceWebhookHandler(
    IWooCommerceWebhookSettings settings,
    OrderOutcome outcome,
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

        var context = Context(topicHeader!, deliveryId);
        var read = WooCommerceOrderPayload.Read(topic, command.Body);
        if (read.Snapshot is not { } snapshot)
        {
            events.Unreadable(topicHeader, deliveryId, read.Problem ?? "nieczytelna treść");
            if (read.ExternalId is not null)
            {
                await outcome.RecordUnreadableAsync(read, OrderErrorSource.Webhook, context, cancellationToken);
            }

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

        // Wyjątek zapisu loguje OrderOutcome (raz); nieudany zapis historii błędu nie zmienia kodu odpowiedzi.
        var applied = await outcome.ApplyAsync(snapshot, OrderErrorSource.Webhook, context, cancellationToken);
        if (applied.SyncResult is { } result)
        {
            events.Applied(topicHeader!, deliveryId, snapshot.ExternalId, result);
        }

        return applied.Status == OrderApplyStatus.Applied ? WebhookOutcome.Accepted : WebhookOutcome.Unavailable;
    }

    /// <summary>Kontekst błędu: temat i ID dostawy (nagłówki już przycięte do 64 znaków).</summary>
    private static string Context(string topic, string? deliveryId) =>
        deliveryId is null ? topic : $"{topic}, dostawa {deliveryId}";

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
