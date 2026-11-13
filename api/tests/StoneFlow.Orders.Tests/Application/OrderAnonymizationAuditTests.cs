using StoneFlow.Orders.Application;
using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Orders.Tests.Application;

/// <summary>SF-22: pola anonimizowane w audycie i wpisy operacji anonimizacji.</summary>
public sealed class OrderAnonymizationAuditTests
{
    private static readonly Guid AccountId = Guid.Parse("4c2f8a3e-1b7d-4e55-9a0f-2d6b1c3e5f70");

    [Fact]
    public void Selectors_match_names_in_stored_entries()
    {
        Assert.Equal(
            [
                new AuditFieldSelector(AuditObjectType.Order, "customerName"),
                new AuditFieldSelector(AuditObjectType.Order, "customerPhone"),
                new AuditFieldSelector(AuditObjectType.Order, "customerEmail"),
                new AuditFieldSelector(AuditObjectType.Order, "customerStreet"),
                new AuditFieldSelector(AuditObjectType.Order, "customerPostalCode"),
                new AuditFieldSelector(AuditObjectType.Order, "customerCity"),
                new AuditFieldSelector(AuditObjectType.Order, "customerTaxId"),
                new AuditFieldSelector(AuditObjectType.Order, "notes"),
                new AuditFieldSelector(AuditObjectType.Order, "comment"),
                new AuditFieldSelector(AuditObjectType.Refund, "reason"),
                new AuditFieldSelector(AuditObjectType.SalesCorrection, "reason"),
            ],
            OrderAnonymizationAudit.Fields);

        // Każde pole danych osobowych zamówienia w audycie jest objęte anonimizacją.
        var created = OrderAudit.Created(TestOrders.Manual(), AccountId);
        Assert.All(
            created.Changes.Where(change => change.IsPersonalData),
            change => Assert.Contains(new AuditFieldSelector(AuditObjectType.Order, change.Field), OrderAnonymizationAudit.Fields));
    }

    [Fact]
    public void Order_entry_has_scope_without_personal_data()
    {
        var order = TestOrders.Manual();

        var entry = OrderAnonymizationAudit.Anonymized(
            order, [OrderField.CustomerName, OrderField.CustomerEmail, OrderField.Notes], 2, new DateOnly(2021, 3, 15), 60, AccountId);

        Assert.Equal(AuditOperation.Anonymized, entry.Operation);
        Assert.Equal(AuditAuthor.Account(AccountId), entry.Author);
        Assert.Equal(new AuditObject(AuditObjectType.Order, order.Id, order.OrderDate, order.Number), entry.Object);
        Assert.Equal(
            [
                new AuditChange("anonymizedFields", null, """["customerName","customerEmail","notes"]"""),
                new AuditChange("anonymizedReasons", null, "2"),
                new AuditChange("retentionStartDate", null, "2021-03-15"),
                new AuditChange("retentionMonths", null, "60"),
            ],
            entry.Changes);
    }

    [Fact]
    public void Empty_field_list_is_an_empty_json_array()
    {
        var entry = OrderAnonymizationAudit.Anonymized(TestOrders.Manual(), [], 0, new DateOnly(2021, 3, 15), 1, AccountId);

        Assert.Equal("[]", entry.Changes[0].NewValue);
        Assert.Equal("0", entry.Changes[1].NewValue);
    }

    [Fact]
    public void Entry_operation_points_to_the_order()
    {
        var refund = TestPayments.NewRefund();
        var correction = TestPayments.NewCorrection();

        var refundEntry = OrderAnonymizationAudit.EntryAnonymized(refund, "ZR-2026-0001", AccountId);
        var correctionEntry = OrderAnonymizationAudit.EntryAnonymized(correction, "ZR-2026-0001", AccountId);

        Assert.Equal(
            new AuditObject(AuditObjectType.Refund, refund.Id, refund.Date, "Zwrot do ZR-2026-0001", new AuditObjectReference(AuditObjectType.Order, TestPayments.OrderId)),
            refundEntry.Object);
        Assert.Equal(AuditObjectType.SalesCorrection, correctionEntry.Object.Type);
        Assert.Equal(AuditOperation.Anonymized, refundEntry.Operation);
        Assert.Equal(new AuditChange("anonymizedFields", null, """["reason"]"""), Assert.Single(refundEntry.Changes));
    }
}
