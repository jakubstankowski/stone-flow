using StoneFlow.Orders.Application;
using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Orders.Tests.Application;

public sealed class OrderAuditTests
{
    private static readonly Guid AccountId = Guid.Parse("4c2f8a3e-1b7d-4e55-9a0f-2d6b1c3e5f70");

    [Fact]
    public void Created_lists_new_values_without_empty_fields_and_with_false_flags()
    {
        var content = TestOrders.Content() with
        {
            Customer = new Customer(CustomerKind.Person, "Jan Testowy", null, null, null, null, null, "1234563218"),
            Operational = new OperationalData(["Dostawca A", "Dostawca „B”"], [], null, HasPhotos: false, HasReview: true, Notes: null),
            Comment = null,
        };
        var order = Order.CreateManual(Guid.NewGuid(), "ZR-2026-0007", content, Guid.NewGuid(), new byte[32], TestOrders.CreatedAt);

        var entry = OrderAudit.Created(order, AccountId);

        Assert.Equal(AuditAuthor.Account(AccountId), entry.Author);
        Assert.Equal(new AuditObject(AuditObjectType.Order, order.Id, new DateOnly(2026, 10, 28), "ZR-2026-0007"), entry.Object);
        Assert.Equal(AuditOperation.Created, entry.Operation);
        Assert.Equal(
            [
                new AuditChange("orderDate", null, "2026-10-28"),
                new AuditChange("channel", null, "olx"),
                new AuditChange("value", null, "1234.50"),
                new AuditChange("fulfillmentStatus", null, "inProgress"),
                new AuditChange("customerKind", null, "person"),
                new AuditChange("customerName", null, "Jan Testowy", IsPersonalData: true),
                new AuditChange("customerTaxId", null, "1234563218", IsPersonalData: true),
                new AuditChange("suppliers", null, "[\"Dostawca A\",\"Dostawca „B”\"]"),
                new AuditChange("hasPhotos", null, "false"),
                new AuditChange("hasReview", null, "true"),
            ],
            entry.Changes);
    }

    [Fact]
    public void Updated_lists_only_changed_fields_with_old_and_new_values()
    {
        var order = TestOrders.Manual();
        var result = order.Edit(
            TestOrders.SameAs(order) with
            {
                Value = 1300m,
                OrderDate = new DateOnly(2026, 11, 2),
                Customer = TestOrders.Customer() with { Phone = null },
                Operational = TestOrders.Operational(),
            },
            TestOrders.Later);

        var entry = OrderAudit.Updated(order, result.Changes, AccountId);

        Assert.Equal(AuditOperation.Updated, entry.Operation);
        Assert.Equal(new DateOnly(2026, 11, 2), entry.Object.BusinessDate);
        Assert.Equal("ZR-2026-0001", entry.Object.Label);
        Assert.Equal(
            [
                new AuditChange("orderDate", "2026-10-28", "2026-11-02"),
                new AuditChange("value", "1234.50", "1300.00"),
                new AuditChange("customerPhone", "500 600 700", null, IsPersonalData: true),
                new AuditChange("suppliers", "[\"Dostawca A\",\"Dostawca B\"]", null),
            ],
            entry.Changes);
    }

    [Fact]
    public void Status_change_is_recorded_as_codes()
    {
        var order = TestOrders.Manual();

        var entry = OrderAudit.Updated(order, order.Cancel(TestOrders.Later).Changes, AccountId);

        Assert.Equal([new AuditChange("fulfillmentStatus", "inProgress", "cancelled")], entry.Changes);
    }

    [Theory]
    [InlineData(OrderField.CustomerKind, false)]
    [InlineData(OrderField.CustomerName, true)]
    [InlineData(OrderField.CustomerEmail, true)]
    [InlineData(OrderField.CustomerTaxId, true)]
    [InlineData(OrderField.Value, false)]
    [InlineData(OrderField.Notes, false)]
    public void Customer_fields_except_kind_are_personal_data(OrderField field, bool personal) =>
        Assert.Equal(personal, OrderAudit.IsPersonalData(field));
}
