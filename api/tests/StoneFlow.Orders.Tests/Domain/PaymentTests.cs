using StoneFlow.Orders.Domain.Payments;

namespace StoneFlow.Orders.Tests.Domain;

public sealed class PaymentTests
{
    [Fact]
    public void Payment_keeps_details_and_creation_request()
    {
        var key = Guid.NewGuid();
        var payment = TestPayments.NewPayment(key: key);

        Assert.Equal(TestPayments.OrderId, payment.OrderId);
        Assert.Equal(300m, payment.Amount);
        Assert.Equal(new DateOnly(2026, 11, 2), payment.Date);
        Assert.Equal(PaymentMethod.Cash, payment.Method);
        Assert.Equal("Anna Przykładowa", payment.ReceivedBy);
        Assert.Equal(key, payment.CreationRequestId);
        Assert.Equal(TestOrders.CreatedAt, payment.CreatedAtUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Amount_must_be_positive(decimal amount)
    {
        Assert.Throws<ArgumentException>(() => TestPayments.NewPayment(amount: amount));
        Assert.Throws<ArgumentException>(() => TestPayments.NewRefund(amount: amount));
    }

    [Fact]
    public void Received_by_is_allowed_only_for_cash()
    {
        Assert.Throws<ArgumentException>(() =>
            Payment.Create(Guid.NewGuid(), TestPayments.OrderId, new PaymentDetails(10m, new DateOnly(2026, 11, 2), PaymentMethod.Transfer, "Anna"), Guid.NewGuid(), new byte[32], TestOrders.CreatedAt));
    }

    [Fact]
    public void Refund_requires_reason()
    {
        Assert.Throws<ArgumentException>(() =>
            Refund.Create(Guid.NewGuid(), TestPayments.OrderId, new RefundDetails(RefundKind.MoneyReturn, 10m, new DateOnly(2026, 11, 2), RefundMethod.Blik, " "), Guid.NewGuid(), new byte[32], TestOrders.CreatedAt));
    }

    [Fact]
    public void Payment_edit_returns_only_changed_fields()
    {
        var payment = TestPayments.NewPayment();

        var changes = payment.Edit(new PaymentDetails(350m, new DateOnly(2026, 11, 2), PaymentMethod.Transfer, null), TestOrders.Later);

        Assert.Equal(
            [
                new EntryFieldChange(EntryField.Amount, 300m, 350m),
                new EntryFieldChange(EntryField.Method, PaymentMethod.Cash, PaymentMethod.Transfer),
                new EntryFieldChange(EntryField.ReceivedBy, "Anna Przykładowa", null),
            ],
            changes);
        Assert.Equal(TestOrders.Later, payment.UpdatedAtUtc);
    }

    [Fact]
    public void Edit_without_changes_keeps_update_time()
    {
        var payment = TestPayments.NewPayment();

        var changes = payment.Edit(new PaymentDetails(300m, new DateOnly(2026, 11, 2), PaymentMethod.Cash, "Anna Przykładowa"), TestOrders.Later);

        Assert.Empty(changes);
        Assert.Equal(TestOrders.CreatedAt, payment.UpdatedAtUtc);
    }

    [Fact]
    public void Refund_kind_can_be_changed()
    {
        var refund = TestPayments.NewRefund();

        var changes = refund.Edit(
            new RefundDetails(RefundKind.SalesReturn, 200m, new DateOnly(2026, 11, 3), RefundMethod.Transfer, "Zwrot nadpłaty"), TestOrders.Later);

        Assert.Equal([new EntryFieldChange(EntryField.Kind, RefundKind.MoneyReturn, RefundKind.SalesReturn)], changes);
        Assert.Equal(RefundKind.SalesReturn, refund.Kind);
    }

    [Fact]
    public void Snapshot_lists_all_audited_fields_in_order()
    {
        Assert.Equal(
            [EntryField.Amount, EntryField.Date, EntryField.Method, EntryField.ReceivedBy],
            TestPayments.NewPayment().Snapshot().Select(field => field.Field));
        Assert.Equal(
            [EntryField.Kind, EntryField.Amount, EntryField.Date, EntryField.Method, EntryField.Reason],
            TestPayments.NewRefund().Snapshot().Select(field => field.Field));
    }
}
