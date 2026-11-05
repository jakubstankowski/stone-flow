using StoneFlow.Orders.Domain.Orders;

namespace StoneFlow.Orders.Tests.Domain;

public sealed class OrderTests
{
    [Fact]
    public void Manual_order_has_manual_source_given_number_and_status()
    {
        var order = TestOrders.Manual();

        Assert.Equal(OrderSource.Manual, order.Source);
        Assert.Null(order.ExternalId);
        Assert.Equal("ZR-2026-0001", order.Number);
        Assert.Equal(FulfillmentStatus.InProgress, order.FulfillmentStatus);
        Assert.Equal(TestOrders.CreatedAt, order.CreatedAtUtc);
        Assert.Equal(TestOrders.CreatedAt, order.UpdatedAtUtc);
        Assert.Empty(order.ShopOwnedFields);
    }

    [Fact]
    public void Manual_order_cannot_be_created_cancelled_or_with_negative_value()
    {
        Assert.Throws<ArgumentException>(() => TestOrders.Manual(FulfillmentStatus.Cancelled));
        Assert.Throws<ArgumentException>(() =>
            Order.CreateManual(Guid.NewGuid(), "ZR-2026-0001", TestOrders.Content(value: -0.01m), Guid.NewGuid(), new byte[32], TestOrders.CreatedAt));
    }

    [Theory]
    [InlineData(1, "ZR-2026-0001")]
    [InlineData(42, "ZR-2026-0042")]
    [InlineData(10000, "ZR-2026-10000")]
    public void Number_has_at_least_four_digits(int sequence, string expected) =>
        Assert.Equal(expected, Order.FormatNumber(2026, sequence));

    [Fact]
    public void Edit_returns_only_changed_fields_with_old_and_new_values()
    {
        var order = TestOrders.Manual();
        var edit = TestOrders.SameAs(order) with { Value = 1300m, OrderDate = new DateOnly(2026, 11, 2), Comment = null };

        var result = order.Edit(edit, TestOrders.Later);

        Assert.True(result.Succeeded);
        Assert.Equal([OrderField.OrderDate, OrderField.Value, OrderField.Comment], result.Changes.Select(c => c.Field));
        var value = result.Changes.Single(c => c.Field == OrderField.Value);
        Assert.Equal(1234.50m, value.OldValue);
        Assert.Equal(1300m, value.NewValue);
        Assert.Equal(1300m, order.Value);
        Assert.Equal(new DateOnly(2026, 11, 2), order.OrderDate);
        Assert.Equal(TestOrders.Later, order.UpdatedAtUtc);
    }

    [Fact]
    public void Edit_detects_list_order_and_customer_field_changes()
    {
        var order = TestOrders.Manual();
        var edit = TestOrders.SameAs(order) with
        {
            Operational = TestOrders.Operational("Dostawca B", "Dostawca A"),
            Customer = TestOrders.Customer("Anna Przykładowa"),
        };

        var result = order.Edit(edit, TestOrders.Later);

        Assert.Equal([OrderField.CustomerName, OrderField.Suppliers], result.Changes.Select(c => c.Field));
    }

    [Fact]
    public void Edit_without_changes_returns_empty_list_and_keeps_timestamp()
    {
        var order = TestOrders.Manual();
        var edit = TestOrders.SameAs(order) with
        {
            // Nowe instancje o tych samych wartościach nie są zmianą.
            Operational = TestOrders.Operational("Dostawca A", "Dostawca B"),
            Customer = TestOrders.Customer(),
            Value = 1234.5m,
        };

        var result = order.Edit(edit, TestOrders.Later);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Changes);
        Assert.Equal(TestOrders.CreatedAt, order.UpdatedAtUtc);
    }

    [Fact]
    public void Edit_cannot_set_cancelled_on_an_active_order()
    {
        var order = TestOrders.Manual();

        var result = order.Edit(TestOrders.SameAs(order) with { FulfillmentStatus = FulfillmentStatus.Cancelled, Value = 1m }, TestOrders.Later);

        Assert.Equal(OrderRuleViolationKind.StatusChangeRequiresAction, result.Violation?.Kind);
        Assert.Equal(FulfillmentStatus.InProgress, order.FulfillmentStatus);
        Assert.Equal(1234.50m, order.Value);
    }

    [Theory]
    [InlineData(FulfillmentStatus.InProgress)]
    [InlineData(FulfillmentStatus.Completed)]
    public void Edit_of_cancelled_order_requires_cancelled_status(FulfillmentStatus status)
    {
        var order = TestOrders.Manual();
        order.Cancel(TestOrders.CreatedAt);

        var rejected = order.Edit(TestOrders.SameAs(order) with { FulfillmentStatus = status }, TestOrders.Later);
        var accepted = order.Edit(TestOrders.SameAs(order) with { Comment = "Poprawka" }, TestOrders.Later);

        Assert.Equal(OrderRuleViolationKind.StatusChangeRequiresAction, rejected.Violation?.Kind);
        Assert.True(accepted.Succeeded);
        Assert.Equal(OrderField.Comment, Assert.Single(accepted.Changes).Field);
        Assert.Equal(FulfillmentStatus.Cancelled, order.FulfillmentStatus);
    }

    [Fact]
    public void Edit_of_manual_order_requires_shop_fields()
    {
        var order = TestOrders.Manual();

        Assert.Throws<ArgumentException>(() => order.Edit(TestOrders.SameAs(order) with { Value = null }, TestOrders.Later));
    }

    [Fact]
    public void Shop_order_rejects_changes_of_shop_owned_fields_including_channel()
    {
        var order = TestOrders.FromShop();
        var edit = TestOrders.SameAs(order) with
        {
            OrderDate = new DateOnly(2026, 1, 1),
            Value = 1m,
            Channel = OrderChannel.Phone,
            Customer = TestOrders.Customer("Anna Przykładowa"),
        };

        var result = order.Edit(edit, TestOrders.Later);

        Assert.Equal(OrderRuleViolationKind.FieldOwnedByShop, result.Violation?.Kind);
        Assert.Equal(Order.AllShopOwnedFields, result.Violation?.Fields);
        Assert.Equal(1234.50m, order.Value);
        Assert.Equal(Order.AllShopOwnedFields, order.ShopOwnedFields);
    }

    [Fact]
    public void Shop_order_treats_unreadable_shop_fields_as_changed()
    {
        var order = TestOrders.FromShop();
        var edit = TestOrders.SameAs(order) with
        {
            Value = 1m,
            Channel = null,
            UnreadableShopFields = [ShopOwnedField.Channel],
        };

        var result = order.Edit(edit, TestOrders.Later);

        Assert.Equal(OrderRuleViolationKind.FieldOwnedByShop, result.Violation?.Kind);
        Assert.Equal([ShopOwnedField.Value, ShopOwnedField.Channel], result.Violation?.Fields);
    }

    [Fact]
    public void Shop_order_accepts_equal_or_missing_shop_fields_and_changes_erp_fields()
    {
        var order = TestOrders.FromShop();
        var equal = TestOrders.SameAs(order) with { Comment = "Uwaga ERP" };
        var missing = new OrderEdit(order.FulfillmentStatus, TestOrders.Operational("Dostawca C"), "Uwaga ERP", null, null, null, null);

        var first = order.Edit(equal, TestOrders.Later);
        var second = order.Edit(missing, TestOrders.Later);

        Assert.Equal(OrderField.Comment, Assert.Single(first.Changes).Field);
        Assert.Equal(OrderField.Suppliers, Assert.Single(second.Changes).Field);
        Assert.Equal(1234.50m, order.Value);
        Assert.Equal(OrderChannel.Shop, order.Channel);
    }

    [Fact]
    public void Cancel_and_restore_change_status_and_reject_wrong_state()
    {
        var order = TestOrders.Manual(FulfillmentStatus.Problem);

        var restoreActive = order.Restore(TestOrders.Later);
        var cancel = order.Cancel(TestOrders.Later);
        var cancelAgain = order.Cancel(TestOrders.Later);
        Assert.Equal(FulfillmentStatus.Cancelled, order.FulfillmentStatus);
        var restore = order.Restore(TestOrders.Later);

        Assert.Equal(OrderRuleViolationKind.NotCancelled, restoreActive.Violation?.Kind);
        var cancelled = Assert.Single(cancel.Changes);
        Assert.Equal(new OrderFieldChange(OrderField.FulfillmentStatus, FulfillmentStatus.Problem, FulfillmentStatus.Cancelled), cancelled);
        Assert.Equal(OrderRuleViolationKind.AlreadyCancelled, cancelAgain.Violation?.Kind);
        Assert.Equal(
            new OrderFieldChange(OrderField.FulfillmentStatus, FulfillmentStatus.Cancelled, FulfillmentStatus.InProgress),
            Assert.Single(restore.Changes));
        Assert.Equal(FulfillmentStatus.InProgress, order.FulfillmentStatus);
    }
}
