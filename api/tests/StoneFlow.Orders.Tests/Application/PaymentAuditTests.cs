using StoneFlow.Orders.Application.Payments;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Orders.Tests.Application;

public sealed class PaymentAuditTests
{
    private static readonly Guid AccountId = Guid.Parse("4c2f8a3e-1b7d-4e55-9a0f-2d6b1c3e5f70");

    [Fact]
    public void Payment_created_has_label_parent_business_date_and_personal_received_by()
    {
        var payment = TestPayments.NewPayment();

        var entry = PaymentAudit.Created(payment, "ZR-2026-0007", AccountId);

        Assert.Equal(AuditAuthor.Account(AccountId), entry.Author);
        Assert.Equal(
            new AuditObject(
                AuditObjectType.Payment,
                payment.Id,
                new DateOnly(2026, 11, 2),
                "Wpłata do ZR-2026-0007",
                new AuditObjectReference(AuditObjectType.Order, TestPayments.OrderId)),
            entry.Object);
        Assert.Equal(AuditOperation.Created, entry.Operation);
        Assert.Equal(
            [
                new AuditChange("amount", null, "300.00"),
                new AuditChange("date", null, "2026-11-02"),
                new AuditChange("method", null, "cash"),
                new AuditChange("receivedBy", null, "Anna Przykładowa", IsPersonalData: true),
            ],
            entry.Changes);
    }

    [Fact]
    public void Created_skips_empty_fields()
    {
        var payment = Payment.Create(
            Guid.NewGuid(),
            TestPayments.OrderId,
            new PaymentDetails(10m, new DateOnly(2026, 11, 2), PaymentMethod.CashOnDelivery, null),
            Guid.NewGuid(),
            new byte[32],
            TestOrders.CreatedAt);

        var entry = PaymentAudit.Created(payment, "ZR-2026-0007", AccountId);

        Assert.DoesNotContain(entry.Changes, change => change.Field == "receivedBy");
        Assert.Contains(entry.Changes, change => change.Field == "method" && change.NewValue == "cashOnDelivery");
    }

    [Fact]
    public void Refund_updated_lists_changed_fields_with_new_business_date()
    {
        var refund = TestPayments.NewRefund();
        var changes = refund.Edit(
            new RefundDetails(RefundKind.SalesReturn, 250m, new DateOnly(2026, 11, 4), RefundMethod.Transfer, "Zwrot nadpłaty"), TestOrders.Later);

        var entry = PaymentAudit.Updated(refund, changes, "ZR-2026-0007", AccountId);

        Assert.Equal(AuditObjectType.Refund, entry.Object.Type);
        Assert.Equal("Zwrot do ZR-2026-0007", entry.Object.Label);
        Assert.Equal(new DateOnly(2026, 11, 4), entry.Object.BusinessDate);
        Assert.Equal(new AuditObjectReference(AuditObjectType.Order, TestPayments.OrderId), entry.Object.Parent);
        Assert.Equal(AuditOperation.Updated, entry.Operation);
        Assert.Equal(
            [
                new AuditChange("kind", "moneyReturn", "salesReturn"),
                new AuditChange("amount", "200.00", "250.00"),
                new AuditChange("date", "2026-11-03", "2026-11-04"),
            ],
            entry.Changes);
    }

    [Fact]
    public void Deleted_records_all_fields_as_old_values()
    {
        var payment = TestPayments.NewPayment();
        var refund = TestPayments.NewRefund();

        var deletedPayment = PaymentAudit.Deleted(payment, "ZR-2026-0007", AccountId);
        var deletedRefund = PaymentAudit.Deleted(refund, "ZR-2026-0007", AccountId);

        Assert.Equal(AuditOperation.Deleted, deletedPayment.Operation);
        Assert.Equal(
            [
                new AuditChange("amount", "300.00", null),
                new AuditChange("date", "2026-11-02", null),
                new AuditChange("method", "cash", null),
                new AuditChange("receivedBy", "Anna Przykładowa", null, IsPersonalData: true),
            ],
            deletedPayment.Changes);
        Assert.Equal(
            [
                new AuditChange("kind", "moneyReturn", null),
                new AuditChange("amount", "200.00", null),
                new AuditChange("date", "2026-11-03", null),
                new AuditChange("method", "transfer", null),
                new AuditChange("reason", "Zwrot nadpłaty", null),
            ],
            deletedRefund.Changes);
    }

    [Theory]
    [InlineData(EntryField.ReceivedBy, true)]
    [InlineData(EntryField.Reason, false)]
    [InlineData(EntryField.Amount, false)]
    [InlineData(EntryField.Kind, false)]
    public void Only_received_by_is_personal_data(EntryField field, bool personal) =>
        Assert.Equal(personal, PaymentAudit.IsPersonalData(field));
}
