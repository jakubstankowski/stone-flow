using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Orders.Domain.Payments;

namespace StoneFlow.Orders.Tests.Domain;

/// <summary>SF-22: anonimizacja powodu zwrotu i korekty sprzedaży.</summary>
public sealed class EntryAnonymizationTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 10, 15, 0, DateTimeKind.Utc);

    [Fact]
    public void Refund_reason_is_replaced_and_hash_erased()
    {
        var refund = Refund.Create(
            Guid.NewGuid(),
            TestPayments.OrderId,
            new RefundDetails(RefundKind.SalesReturn, 200m, new DateOnly(2026, 11, 3), RefundMethod.Transfer, "Zwrot dla Jana Testowego"),
            Guid.NewGuid(),
            [.. Enumerable.Repeat((byte)5, 32)],
            TestOrders.CreatedAt);

        var changes = refund.AnonymizeReason(Now);

        Assert.Equal(EntryField.Reason, Assert.Single(changes).Field);
        Assert.Equal(CustomerAnonymization.Marker, refund.Reason);
        Assert.Equal(new byte[32], refund.CreationRequestHash);
        Assert.Equal((RefundKind.SalesReturn, 200m, new DateOnly(2026, 11, 3), RefundMethod.Transfer), (refund.Kind, refund.Amount, refund.Date, refund.Method));
        Assert.Equal(Now, refund.UpdatedAtUtc);
    }

    [Fact]
    public void Correction_reason_is_replaced_and_hash_erased()
    {
        var correction = SalesCorrection.Create(
            Guid.NewGuid(),
            TestPayments.OrderId,
            new SalesCorrectionDetails(500m, new DateOnly(2026, 11, 3), "Rabat dla Jana Testowego"),
            Guid.NewGuid(),
            [.. Enumerable.Repeat((byte)5, 32)],
            TestOrders.CreatedAt);

        var changes = correction.AnonymizeReason(Now);

        Assert.Equal(EntryField.Reason, Assert.Single(changes).Field);
        Assert.Equal(CustomerAnonymization.Marker, correction.Reason);
        Assert.Equal(new byte[32], correction.CreationRequestHash);
        Assert.Equal((500m, new DateOnly(2026, 11, 3)), (correction.Amount, correction.Date));
        Assert.Equal(Now, correction.UpdatedAtUtc);
    }

    [Fact]
    public void Reason_already_equal_to_marker_is_not_a_change()
    {
        var refund = Refund.Create(
            Guid.NewGuid(),
            TestPayments.OrderId,
            new RefundDetails(RefundKind.MoneyReturn, 200m, new DateOnly(2026, 11, 3), RefundMethod.Transfer, CustomerAnonymization.Marker),
            Guid.NewGuid(),
            [.. Enumerable.Repeat((byte)5, 32)],
            TestOrders.CreatedAt);

        var changes = refund.AnonymizeReason(Now);

        Assert.Empty(changes);
        Assert.Equal(TestOrders.CreatedAt, refund.UpdatedAtUtc);
        Assert.Equal(new byte[32], refund.CreationRequestHash);
    }

    [Fact]
    public void Edit_after_anonymization_keeps_marker_unless_reason_changes()
    {
        var refund = TestPayments.NewRefund();
        refund.AnonymizeReason(Now);
        var same = new RefundDetails(refund.Kind, refund.Amount, refund.Date, refund.Method, refund.Reason);

        Assert.Empty(refund.Edit(same, Now.AddDays(1)));

        var changes = refund.Edit(same with { Reason = "Nowy powód" }, Now.AddDays(2));
        Assert.Equal(EntryField.Reason, Assert.Single(changes).Field);
        Assert.Equal("Nowy powód", refund.Reason);
    }
}
