using StoneFlow.Orders.Domain.Orders;

namespace StoneFlow.Orders.Tests.Domain;

/// <summary>SF-22: anonimizacja danych klienta zamówienia i blokada ich zmiany w edycji.</summary>
public sealed class OrderAnonymizationTests
{
    private static readonly Guid AccountId = Guid.Parse("4c2f8a3e-1b7d-4e55-9a0f-2d6b1c3e5f70");
    private static readonly DateTime Now = new(2026, 11, 13, 10, 15, 0, DateTimeKind.Utc);

    private static readonly OrderField[] AllClearedFields =
    [
        OrderField.CustomerName,
        OrderField.CustomerPhone,
        OrderField.CustomerEmail,
        OrderField.CustomerStreet,
        OrderField.CustomerPostalCode,
        OrderField.CustomerCity,
        OrderField.CustomerTaxId,
        OrderField.Notes,
        OrderField.Comment,
    ];

    private static Order FullManual()
    {
        var content = TestOrders.Content() with
        {
            Operational = TestOrders.Operational("Dostawca A") with { InvoiceNumber = "FV/1/2026", Notes = "Dzwonić po 16" },
        };
        return Order.CreateManual(Guid.NewGuid(), "ZR-2026-0001", content, Guid.NewGuid(), [.. Enumerable.Repeat((byte)7, 32)], TestOrders.CreatedAt);
    }

    [Fact]
    public void Clears_customer_data_notes_and_comment()
    {
        var order = FullManual();

        var result = order.AnonymizeCustomer(AccountId, Now);

        Assert.True(result.Succeeded);
        Assert.Equal(new Customer(CustomerKind.Person, CustomerAnonymization.Marker, null, null, null, null, null, null), order.Customer);
        Assert.Null(order.Operational.Notes);
        Assert.Null(order.Comment);
    }

    [Fact]
    public void Keeps_kind_amounts_dates_and_operational_fields()
    {
        var order = FromShopWithData();
        var before = (order.Customer.Kind, order.OrderDate, order.Channel, order.Value, order.FulfillmentStatus, order.Source, order.Number,
            order.ExternalId, order.ShopStatus, order.ShopModifiedAtUtc, order.ShopDeletedAtUtc, order.ShopRefundedAmount, order.CreatedAtUtc);
        var suppliers = order.Operational.Suppliers;
        var zo = order.Operational.ZoReferences;

        order.AnonymizeCustomer(AccountId, Now);

        Assert.Equal(before, (order.Customer.Kind, order.OrderDate, order.Channel, order.Value, order.FulfillmentStatus, order.Source, order.Number,
            order.ExternalId, order.ShopStatus, order.ShopModifiedAtUtc, order.ShopDeletedAtUtc, order.ShopRefundedAmount, order.CreatedAtUtc));
        Assert.Equal(suppliers, order.Operational.Suppliers);
        Assert.Equal(zo, order.Operational.ZoReferences);
        Assert.Equal("FV/1/2026", order.Operational.InvoiceNumber);
        Assert.True(order.Operational.HasReview);
        Assert.False(order.Operational.HasPhotos);
    }

    [Fact]
    public void Lists_only_fields_that_had_a_value()
    {
        var minimal = Order.CreateManual(
            Guid.NewGuid(),
            "ZR-2026-0003",
            TestOrders.Content() with
            {
                Customer = new Customer(CustomerKind.Company, "Firma Testowa", null, null, null, null, null, null),
                Comment = null,
            },
            Guid.NewGuid(),
            new byte[32],
            TestOrders.CreatedAt);
        var full = FullManual();

        var minimalChanges = minimal.AnonymizeCustomer(AccountId, Now).Changes;
        var fullChanges = full.AnonymizeCustomer(AccountId, Now).Changes;

        Assert.Equal([OrderField.CustomerName], minimalChanges.Select(change => change.Field));
        Assert.Equal(AllClearedFields, fullChanges.Select(change => change.Field));
        Assert.Equal(CustomerKind.Company, minimal.Customer.Kind);
    }

    [Fact]
    public void Name_already_equal_to_marker_is_not_listed()
    {
        var order = Order.CreateManual(
            Guid.NewGuid(),
            "ZR-2026-0004",
            TestOrders.Content() with
            {
                Customer = new Customer(CustomerKind.Person, CustomerAnonymization.Marker, null, null, null, null, null, null),
                Comment = null,
            },
            Guid.NewGuid(),
            new byte[32],
            TestOrders.CreatedAt);

        var result = order.AnonymizeCustomer(AccountId, Now);

        Assert.Empty(result.Changes);
        Assert.True(order.IsCustomerAnonymized);
        Assert.Equal(Now, order.UpdatedAtUtc);
    }

    [Fact]
    public void Erases_hash_and_records_the_fact()
    {
        var manual = FullManual();
        var shop = FromShopWithData();

        manual.AnonymizeCustomer(AccountId, Now);
        shop.AnonymizeCustomer(AccountId, Now);

        Assert.Equal(new byte[32], manual.CreationRequestHash);
        Assert.Null(shop.CreationRequestHash);
        Assert.Equal(Now, manual.AnonymizedAtUtc);
        Assert.Equal(AccountId, manual.AnonymizedByAccountId);
        Assert.Equal(Now, manual.UpdatedAtUtc);
        Assert.True(manual.IsCustomerAnonymized);
    }

    [Fact]
    public void Second_call_is_rejected_without_changes()
    {
        var order = FullManual();
        order.AnonymizeCustomer(AccountId, Now);
        var snapshot = order.Snapshot();

        var result = order.AnonymizeCustomer(AccountId, Now.AddDays(1));

        Assert.Equal(OrderRuleViolationKind.AlreadyAnonymized, result.Violation?.Kind);
        Assert.Equal(snapshot, order.Snapshot());
        Assert.Equal(Now, order.UpdatedAtUtc);
        Assert.Equal(Now, order.AnonymizedAtUtc);
    }

    [Fact]
    public void Account_is_required() =>
        Assert.Throws<ArgumentException>(() => FullManual().AnonymizeCustomer(Guid.Empty, Now));

    [Fact]
    public void Edit_with_other_customer_is_rejected()
    {
        var order = FullManual();
        order.AnonymizeCustomer(AccountId, Now);
        var snapshot = order.Snapshot();

        var result = order.Edit(TestOrders.SameAs(order) with { Customer = TestOrders.Customer("Jan Testowy") }, Now.AddDays(1));

        Assert.Equal(OrderRuleViolationKind.CustomerAnonymized, result.Violation?.Kind);
        Assert.Equal(snapshot, order.Snapshot());
    }

    [Fact]
    public void Edit_changing_only_customer_kind_is_rejected()
    {
        var order = FullManual();
        order.AnonymizeCustomer(AccountId, Now);

        var result = order.Edit(TestOrders.SameAs(order) with { Customer = order.Customer with { Kind = CustomerKind.Company } }, Now.AddDays(1));

        Assert.Equal(OrderRuleViolationKind.CustomerAnonymized, result.Violation?.Kind);
    }

    [Fact]
    public void Edit_with_same_customer_saves_erp_fields()
    {
        var order = FullManual();
        order.AnonymizeCustomer(AccountId, Now);
        var edit = TestOrders.SameAs(order) with
        {
            FulfillmentStatus = FulfillmentStatus.Completed,
            Operational = order.Operational with { Notes = "Nowe uwagi" },
            Comment = "Nowy komentarz",
            Value = 999m,
            OrderDate = new DateOnly(2026, 10, 1),
        };

        var result = order.Edit(edit, Now.AddDays(1));

        Assert.True(result.Succeeded);
        Assert.Equal(
            [OrderField.OrderDate, OrderField.Value, OrderField.FulfillmentStatus, OrderField.Notes, OrderField.Comment],
            result.Changes.Select(change => change.Field));
        Assert.Equal(CustomerAnonymization.Marker, order.Customer.Name);
    }

    [Fact]
    public void Edit_of_shop_order_with_other_customer_stays_field_owned_by_shop()
    {
        var order = FromShopWithData();
        order.AnonymizeCustomer(AccountId, Now);

        var result = order.Edit(TestOrders.SameAs(order) with { Customer = TestOrders.Customer() }, Now.AddDays(1));

        Assert.Equal(OrderRuleViolationKind.FieldOwnedByShop, result.Violation?.Kind);
        Assert.Equal([ShopOwnedField.Customer], result.Violation!.Fields);
    }

    private static Order FromShopWithData()
    {
        var data = new ShopOrderData(
            TestOrders.CreatedAt, new DateOnly(2026, 11, 5), "processing", 1000m, 0m, TestOrders.Customer());
        var order = Order.ImportFromShop(Guid.NewGuid(), "1001", data, null, TestOrders.CreatedAt);
        order.Edit(
            TestOrders.SameAs(order) with
            {
                Operational = TestOrders.Operational("Dostawca A") with { InvoiceNumber = "FV/1/2026", Notes = "Dzwonić po 16" },
                Comment = "Komentarz",
            },
            TestOrders.CreatedAt);
        return order;
    }
}
