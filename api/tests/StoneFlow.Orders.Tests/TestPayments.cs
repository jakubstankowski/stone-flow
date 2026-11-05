using StoneFlow.Orders.Domain.Payments;

namespace StoneFlow.Orders.Tests;

/// <summary>Fikcyjne wpłaty i zwroty do testów.</summary>
internal static class TestPayments
{
    public static readonly Guid OrderId = Guid.Parse("0b8c6d2e-5f41-4a7b-9c3d-1e2f3a4b5c6d");

    public static Payment NewPayment(decimal amount = 300m, Guid? key = null) =>
        Payment.Create(
            Guid.NewGuid(),
            OrderId,
            new PaymentDetails(amount, new DateOnly(2026, 11, 2), PaymentMethod.Cash, "Anna Przykładowa"),
            key ?? Guid.NewGuid(),
            new byte[32],
            TestOrders.CreatedAt);

    public static Refund NewRefund(decimal amount = 200m) =>
        Refund.Create(
            Guid.NewGuid(),
            OrderId,
            new RefundDetails(RefundKind.MoneyReturn, amount, new DateOnly(2026, 11, 3), RefundMethod.Transfer, "Zwrot nadpłaty"),
            Guid.NewGuid(),
            new byte[32],
            TestOrders.CreatedAt);

    public static SalesCorrection NewCorrection(decimal amount = 500m) =>
        SalesCorrection.Create(
            Guid.NewGuid(),
            OrderId,
            new SalesCorrectionDetails(amount, new DateOnly(2026, 11, 3), "Oddany kamień"),
            Guid.NewGuid(),
            new byte[32],
            TestOrders.CreatedAt);
}
