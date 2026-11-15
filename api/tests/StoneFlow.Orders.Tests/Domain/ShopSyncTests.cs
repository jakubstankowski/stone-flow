using StoneFlow.Orders.Domain.Orders;

namespace StoneFlow.Orders.Tests.Domain;

/// <summary>SF-12: reguły zastosowania dostaw ze sklepu (własność pól, kolejność, przejście na „Anulowane”).</summary>
public sealed class ShopSyncTests
{
    private static readonly DateTime T0 = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);

    private static ShopOrderData Data(string status = "processing", DateTime? modified = null, decimal value = 1000m, decimal refunded = 0m, string name = "Jan Testowy") =>
        new(modified ?? T0, new DateOnly(2026, 11, 5), status, value, refunded, TestOrders.Customer(name));

    private static Order Imported(string status = "processing", DateTime? deletedAt = null) =>
        Order.ImportFromShop(Guid.NewGuid(), "1001", Data(status), deletedAt, T0);

    [Fact]
    public void Import_sets_shop_number_channel_and_in_progress()
    {
        var order = Imported();

        Assert.Equal("WC-1001", order.Number);
        Assert.Equal(OrderSource.WooCommerce, order.Source);
        Assert.Equal(OrderChannel.Shop, order.Channel);
        Assert.Equal(FulfillmentStatus.InProgress, order.FulfillmentStatus);
        Assert.Equal(T0, order.ShopModifiedAtUtc);
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("trash")]
    public void Import_of_terminal_order_is_cancelled(string status) =>
        Assert.Equal(FulfillmentStatus.Cancelled, Imported(status).FulfillmentStatus);

    [Fact]
    public void Import_with_deletion_marker_is_cancelled_and_deleted()
    {
        var order = Imported(deletedAt: T0.AddMinutes(5));

        Assert.Equal(FulfillmentStatus.Cancelled, order.FulfillmentStatus);
        Assert.Equal(T0.AddMinutes(5), order.ShopDeletedAtUtc);
    }

    [Fact]
    public void Update_changes_shop_fields_only()
    {
        var order = Imported();
        var operational = order.Operational;

        var result = order.ApplyFromShop(ShopDelivery.Updated, Data(modified: T0.AddMinutes(1), value: 900m, name: "Anna Testowa"), T0.AddMinutes(2));

        Assert.Null(result.Skipped);
        Assert.Equal(900m, order.Value);
        Assert.Equal("Anna Testowa", order.Customer.Name);
        Assert.Same(operational, order.Operational);
        Assert.Equal(FulfillmentStatus.InProgress, order.FulfillmentStatus);
        Assert.Contains(result.Changes, c => c.Field == OrderField.Value);
    }

    [Fact]
    public void Older_delivery_is_skipped()
    {
        var order = Imported();
        order.ApplyFromShop(ShopDelivery.Updated, Data(modified: T0.AddMinutes(5), value: 900m), T0.AddMinutes(5));

        var result = order.ApplyFromShop(ShopDelivery.Updated, Data(modified: T0.AddMinutes(1), value: 1m), T0.AddMinutes(6));

        Assert.Equal(ShopDeliverySkip.Older, result.Skipped);
        Assert.Equal(900m, order.Value);
    }

    [Fact]
    public void Same_delivery_twice_changes_nothing()
    {
        var order = Imported();

        var result = order.ApplyFromShop(ShopDelivery.Updated, Data(), T0.AddMinutes(1));

        Assert.Null(result.Skipped);
        Assert.Empty(result.Changes);
        Assert.False(result.StateChanged);
    }

    [Fact]
    public void Newer_date_without_other_changes_is_saved_but_not_audited()
    {
        var order = Imported();

        var result = order.ApplyFromShop(ShopDelivery.Updated, Data(modified: T0.AddMinutes(3)), T0.AddMinutes(3));

        Assert.Empty(result.Changes);
        Assert.True(result.StateChanged);
        Assert.Equal(T0.AddMinutes(3), order.ShopModifiedAtUtc);
    }

    [Fact]
    public void Cancel_in_shop_cancels_once_and_redelivery_does_not_change_erp_status()
    {
        var order = Imported();

        var first = order.ApplyFromShop(ShopDelivery.Updated, Data("cancelled", T0.AddMinutes(1)), T0.AddMinutes(1));
        Assert.Equal(FulfillmentStatus.Cancelled, order.FulfillmentStatus);
        Assert.Contains(first.Changes, c => c.Field == OrderField.FulfillmentStatus);
        Assert.Contains(first.Changes, c => c.Field == OrderField.ShopStatus);

        // Użytkownik przywraca w ERP; ponowna dostawa tego samego statusu nie anuluje ponownie.
        order.Restore(T0.AddMinutes(2));
        order.ApplyFromShop(ShopDelivery.Updated, Data("cancelled", T0.AddMinutes(1)), T0.AddMinutes(3));
        Assert.Equal(FulfillmentStatus.InProgress, order.FulfillmentStatus);
    }

    [Fact]
    public void Trash_then_cancelled_does_not_cancel_again()
    {
        var order = Imported();
        order.ApplyFromShop(ShopDelivery.Updated, Data("trash", T0.AddMinutes(1)), T0.AddMinutes(1));
        order.Restore(T0.AddMinutes(2));

        order.ApplyFromShop(ShopDelivery.Updated, Data("cancelled", T0.AddMinutes(3)), T0.AddMinutes(3));

        Assert.Equal(FulfillmentStatus.InProgress, order.FulfillmentStatus);
    }

    [Fact]
    public void Deletion_cancels_marks_and_second_deletion_after_erp_restore_changes_nothing()
    {
        var order = Imported();

        var first = order.ApplyFromShop(ShopDelivery.Deleted, null, T0.AddMinutes(1));
        Assert.Equal(FulfillmentStatus.Cancelled, order.FulfillmentStatus);
        Assert.Equal(T0.AddMinutes(1), order.ShopDeletedAtUtc);
        Assert.Contains(first.Changes, c => c.Field == OrderField.ShopDeleted);

        order.Restore(T0.AddMinutes(2));
        var second = order.ApplyFromShop(ShopDelivery.Deleted, null, T0.AddMinutes(3));

        Assert.Equal(FulfillmentStatus.InProgress, order.FulfillmentStatus);
        Assert.Equal(T0.AddMinutes(1), order.ShopDeletedAtUtc);
        Assert.Empty(second.Changes);
    }

    [Theory]
    [InlineData(ShopDelivery.Updated)]
    [InlineData(ShopDelivery.Restored)]
    public void Late_delivery_from_before_deletion_is_skipped(ShopDelivery delivery)
    {
        var order = Imported();
        order.ApplyFromShop(ShopDelivery.Deleted, null, T0.AddMinutes(10));

        var result = order.ApplyFromShop(delivery, Data(modified: T0.AddMinutes(5), value: 1m), T0.AddMinutes(11));

        Assert.Equal(ShopDeliverySkip.BeforeDeletion, result.Skipped);
        Assert.Equal(T0.AddMinutes(10), order.ShopDeletedAtUtc);
    }

    [Fact]
    public void Restore_in_shop_after_deletion_clears_marker_without_changing_erp_status()
    {
        var order = Imported();
        order.ApplyFromShop(ShopDelivery.Deleted, null, T0.AddMinutes(10));

        order.ApplyFromShop(ShopDelivery.Restored, Data("processing", T0.AddMinutes(20)), T0.AddMinutes(20));

        Assert.Null(order.ShopDeletedAtUtc);
        Assert.Equal(FulfillmentStatus.Cancelled, order.FulfillmentStatus);
        Assert.Contains(ShopWarning.ActiveInShop, Warnings(order));
    }

    [Fact]
    public void Erp_cancel_of_active_order_warns_active_in_shop()
    {
        var order = Imported();
        order.Cancel(T0.AddMinutes(1));

        Assert.Equal([ShopWarning.ActiveInShop], Warnings(order));
    }

    [Fact]
    public void Erp_restore_of_order_cancelled_in_shop_warns_cancelled_in_shop()
    {
        var order = Imported("cancelled");
        order.Restore(T0.AddMinutes(1));

        Assert.Equal([ShopWarning.CancelledInShop], Warnings(order));
    }

    [Fact]
    public void Refund_in_shop_is_information_only()
    {
        var order = Imported();

        order.ApplyFromShop(ShopDelivery.Updated, Data("refunded", T0.AddMinutes(1), refunded: 200m), T0.AddMinutes(1));

        Assert.Equal(FulfillmentStatus.InProgress, order.FulfillmentStatus);
        Assert.Equal(200m, order.ShopRefundedAmount);
        Assert.Equal([ShopWarning.RefundedInShop], Warnings(order));
    }

    [Fact]
    public void Manual_order_rejects_shop_delivery_and_has_no_shop_audit_fields()
    {
        var order = TestOrders.Manual();

        Assert.Throws<InvalidOperationException>(() => order.ApplyFromShop(ShopDelivery.Deleted, null, T0));
        Assert.All(
            order.Snapshot().Where(f => f.Field is OrderField.ShopStatus or OrderField.ShopDeleted or OrderField.ShopRefundedAmount),
            field => Assert.Null(field.Value));
    }

    [Theory]
    [InlineData("pending", true)]
    [InlineData("failed", true)]
    [InlineData("checkout-draft", true)]
    [InlineData("processing", false)]
    [InlineData("custom-status", false)]
    [InlineData(null, false)]
    public void Hidden_statuses(string? status, bool hidden) => Assert.Equal(hidden, ShopStatuses.IsHiddenByDefault(status));

    [Theory]
    [InlineData("cancelled", true)]
    [InlineData("trash", true)]
    [InlineData("refunded", false)]
    [InlineData("custom-status", false)]
    public void Terminal_statuses(string status, bool terminal) => Assert.Equal(terminal, ShopStatuses.IsTerminal(status, null));

    private static readonly Guid AccountId = Guid.Parse("4c2f8a3e-1b7d-4e55-9a0f-2d6b1c3e5f70");

    private static readonly OrderField[] CustomerFields =
    [
        OrderField.CustomerKind,
        OrderField.CustomerName,
        OrderField.CustomerPhone,
        OrderField.CustomerEmail,
        OrderField.CustomerStreet,
        OrderField.CustomerPostalCode,
        OrderField.CustomerCity,
        OrderField.CustomerTaxId,
    ];

    [Theory]
    [InlineData(ShopDelivery.Created)]
    [InlineData(ShopDelivery.Updated)]
    [InlineData(ShopDelivery.Restored)]
    public void Delivery_after_anonymization_keeps_customer_and_applies_other_shop_fields(ShopDelivery delivery)
    {
        var order = Imported();
        order.AnonymizeCustomer(AccountId, T0.AddMinutes(1));
        var anonymized = order.Customer;

        var result = order.ApplyFromShop(delivery, Data("cancelled", T0.AddMinutes(5), value: 900m, refunded: 50m, name: "Jan Testowy"), T0.AddMinutes(6));

        Assert.Same(anonymized, order.Customer);
        Assert.Equal(900m, order.Value);
        Assert.Equal("cancelled", order.ShopStatus);
        Assert.Equal(50m, order.ShopRefundedAmount);
        Assert.Equal(FulfillmentStatus.Cancelled, order.FulfillmentStatus);
        Assert.DoesNotContain(result.Changes, change => CustomerFields.Contains(change.Field));
    }

    [Fact]
    public void Deletion_and_restore_after_anonymization_keep_customer()
    {
        var order = Imported();
        order.AnonymizeCustomer(AccountId, T0.AddMinutes(1));
        var anonymized = order.Customer;

        order.ApplyFromShop(ShopDelivery.Deleted, null, T0.AddMinutes(2));
        var restored = order.ApplyFromShop(ShopDelivery.Restored, Data(modified: T0.AddMinutes(3)), T0.AddMinutes(3));

        Assert.Null(order.ShopDeletedAtUtc);
        Assert.Same(anonymized, order.Customer);
        Assert.DoesNotContain(restored.Changes, change => CustomerFields.Contains(change.Field));
    }

    [Fact]
    public void Delivery_differing_only_in_customer_after_anonymization_is_unchanged()
    {
        var order = Imported();
        order.AnonymizeCustomer(AccountId, T0.AddMinutes(1));
        var updatedAt = order.UpdatedAtUtc;

        var result = order.ApplyFromShop(ShopDelivery.Updated, Data(modified: T0.AddMinutes(5), name: "Jan Testowy"), T0.AddMinutes(6));

        Assert.Empty(result.Changes);
        Assert.True(result.StateChanged);
        Assert.Equal(T0.AddMinutes(5), order.ShopModifiedAtUtc);
        Assert.Equal(updatedAt, order.UpdatedAtUtc);
        Assert.Equal(CustomerAnonymization.Marker, order.Customer.Name);
    }

    private static IReadOnlyList<ShopWarning> Warnings(Order order) =>
        ShopStatuses.WarningsFor(order.Source, order.FulfillmentStatus, order.ShopStatus, order.ShopDeletedAtUtc, order.ShopRefundedAmount);
}
