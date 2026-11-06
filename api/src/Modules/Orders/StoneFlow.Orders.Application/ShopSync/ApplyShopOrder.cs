using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Application.LaunchDate;
using StoneFlow.Shared.Application.Persistence;
using StoneFlow.Shared.Application.ShopOrders;

namespace StoneFlow.Orders.Application.ShopSync;

/// <summary>
/// Zastosowanie dostawy ze sklepu (SF-12) w jednej jednostce pracy: blokada po ID zewnętrznym,
/// utworzenie albo zmiana zamówienia według reguł domeny, znacznik usunięcia i audyt z autorem
/// „Synchronizacja WooCommerce”. Nie tworzy wpłat ani wypływów.
/// </summary>
public sealed class ShopOrderSync(
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    IOrderStore orders,
    IShopOrderStore shopOrders,
    ILaunchDate launchDate,
    TimeProvider timeProvider) : IShopOrderSync
{
    public Task<ShopOrderSyncResult> ApplyAsync(ShopOrderSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.ExternalId);
        var data = snapshot.Topic == ShopOrderTopic.Deleted ? null : ToData(snapshot);

        return unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;
                await shopOrders.LockAsync(snapshot.ExternalId, ct);
                var stored = await shopOrders.FindAsync(snapshot.ExternalId, ct);
                return stored is null
                    ? await CreateAsync(snapshot, data, now, ct)
                    : await UpdateAsync(stored, snapshot.Topic, data, now, ct);
            },
            commitWhen: result => result != ShopOrderSyncResult.RetryRequired,
            cancellationToken);
    }

    private async Task<ShopOrderSyncResult> CreateAsync(ShopOrderSnapshot snapshot, ShopOrderData? data, DateTime now, CancellationToken ct)
    {
        if (data is null)
        {
            return await shopOrders.RecordDeletionAsync(snapshot.ExternalId, now, ct) == StoreSaveStatus.DuplicateRequest
                ? ShopOrderSyncResult.RetryRequired
                : ShopOrderSyncResult.DeletionRecorded;
        }

        if (data.OrderDate < launchDate.Value)
        {
            return ShopOrderSyncResult.SkippedBeforeLaunch;
        }

        // Usunięcie odebrane przed utworzeniem: dostawa sprzed niego tworzy zamówienie usunięte.
        var deletion = await shopOrders.FindDeletionAsync(snapshot.ExternalId, ct);
        var deletedAt = deletion is { } received && data.ModifiedAtUtc <= received ? deletion : null;
        if (deletion is not null)
        {
            await shopOrders.RemoveDeletionAsync(snapshot.ExternalId, ct);
        }

        var order = Order.ImportFromShop(Guid.NewGuid(), snapshot.ExternalId, data, deletedAt, now);
        var saved = await orders.AddAsync(order, ct);
        if (saved.Status == StoreSaveStatus.DuplicateRequest)
        {
            return ShopOrderSyncResult.RetryRequired;
        }

        await auditLog.RecordAsync(OrderAudit.Created(order, AuditAuthor.WooCommerceSync), ct);
        return ShopOrderSyncResult.Created;
    }

    private async Task<ShopOrderSyncResult> UpdateAsync(
        StoredOrder stored,
        ShopOrderTopic topic,
        ShopOrderData? data,
        DateTime now,
        CancellationToken ct)
    {
        var result = stored.Order.ApplyFromShop(Delivery(topic), data, now);
        if (result.Skipped is { } skipped)
        {
            return skipped == ShopDeliverySkip.Older ? ShopOrderSyncResult.SkippedOlder : ShopOrderSyncResult.SkippedDeletedInShop;
        }

        if (!result.StateChanged)
        {
            return ShopOrderSyncResult.Unchanged;
        }

        // Blokada po ID zewnętrznym szereguje synchronizację; konflikt wersji oznacza zapis użytkownika
        // w tej samej chwili — wycofanie i ponowienie na świeżych danych.
        var saved = await orders.SaveAsync(stored.Order, stored.Version, ct);
        if (saved.Status != StoreSaveStatus.Saved)
        {
            return ShopOrderSyncResult.RetryRequired;
        }

        if (result.Changes.Count == 0)
        {
            return ShopOrderSyncResult.Unchanged;
        }

        await auditLog.RecordAsync(OrderAudit.Updated(stored.Order, result.Changes, AuditAuthor.WooCommerceSync), ct);
        return ShopOrderSyncResult.Updated;
    }

    private static ShopDelivery Delivery(ShopOrderTopic topic) => topic switch
    {
        ShopOrderTopic.Created => ShopDelivery.Created,
        ShopOrderTopic.Updated => ShopDelivery.Updated,
        ShopOrderTopic.Restored => ShopDelivery.Restored,
        ShopOrderTopic.Deleted => ShopDelivery.Deleted,
        _ => throw new ArgumentOutOfRangeException(nameof(topic), topic, null),
    };

    private static ShopOrderData ToData(ShopOrderSnapshot snapshot)
    {
        if (snapshot.ModifiedAtUtc is not { } modified || snapshot.OrderDate is not { } date
            || string.IsNullOrWhiteSpace(snapshot.Status) || snapshot.Value is not { } value)
        {
            throw new ArgumentException("Dostawa pełnego zamówienia bez wymaganych pól.", nameof(snapshot));
        }

        return new ShopOrderData(
            DateTime.SpecifyKind(modified, DateTimeKind.Utc),
            date,
            Truncate(snapshot.Status, Order.ShopStatusMaxLength)!,
            value,
            snapshot.RefundedAmount,
            ToCustomer(snapshot.ExternalId, snapshot.Customer));
    }

    /// <summary>Klient z limitami długości ERP; brak danych → „Klient sklepu #{id}”.</summary>
    internal static Customer ToCustomer(string externalId, ShopCustomer? customer)
    {
        var name = Truncate(customer?.Name, Customer.NameMaxLength);
        return new Customer(
            customer?.Kind == ShopCustomerKind.Company ? CustomerKind.Company : CustomerKind.Person,
            string.IsNullOrWhiteSpace(name) ? $"Klient sklepu #{externalId}" : name,
            Truncate(customer?.Phone, Customer.PhoneMaxLength),
            Truncate(customer?.Email, Customer.EmailMaxLength),
            Truncate(customer?.Street, Customer.StreetMaxLength),
            Truncate(customer?.PostalCode, Customer.PostalCodeMaxLength),
            Truncate(customer?.City, Customer.CityMaxLength),
            Truncate(customer?.TaxId, Customer.TaxIdMaxLength));
    }

    private static string? Truncate(string? text, int maxLength)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
