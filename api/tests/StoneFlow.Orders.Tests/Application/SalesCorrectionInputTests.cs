using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Application.Payments;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Orders.Tests.Application;

public sealed class SalesCorrectionInputTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid AccountId = Guid.Parse("4c2f8a3e-1b7d-4e55-9a0f-2d6b1c3e5f70");

    private static (SalesCorrectionDetails? Details, IDictionary<string, string[]> Errors) Validate(SalesCorrectionInput input, DateTimeOffset? now = null)
    {
        var errors = new FieldErrors();
        var details = input.Validate(errors, now ?? Now);
        return (details, errors.ToDictionary());
    }

    [Fact]
    public void Valid_correction_is_normalized()
    {
        var (details, errors) = Validate(new SalesCorrectionInput(" 500 ", "2020-01-01", "  Oddany kamień "));

        Assert.Empty(errors);
        Assert.Equal(new SalesCorrectionDetails(500m, new DateOnly(2020, 1, 1), "Oddany kamień"), details);
    }

    [Fact]
    public void Missing_or_invalid_fields_are_reported_under_json_paths()
    {
        var (missing, missingErrors) = Validate(new SalesCorrectionInput(null, null, " "));
        var (_, invalid) = Validate(new SalesCorrectionInput("0", "2026-11-06", new string('p', 501)));

        Assert.Null(missing);
        Assert.Equal(["amount", "date", "reason"], missingErrors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal([EntryMessages.CorrectionReasonRequired], missingErrors[EntryFieldKeys.Reason]);
        Assert.Equal([EntryMessages.AmountNotPositive], invalid[EntryFieldKeys.Amount]);
        Assert.Equal([EntryMessages.DateInFuture], invalid[EntryFieldKeys.Date]);
        Assert.Equal([EntryMessages.CorrectionReasonTooLong], invalid[EntryFieldKeys.Reason]);
    }

    [Fact]
    public void Today_follows_Europe_Warsaw_time()
    {
        var (details, errors) = Validate(new SalesCorrectionInput("10", "2026-11-06", "Powód"), new DateTimeOffset(2026, 11, 5, 23, 30, 0, TimeSpan.Zero));

        Assert.Empty(errors);
        Assert.Equal(new DateOnly(2026, 11, 6), details!.Date);
    }

    [Fact]
    public void Request_hash_uses_canonical_values_and_includes_order()
    {
        var orderId = TestPayments.OrderId;
        var details = new SalesCorrectionDetails(500m, new DateOnly(2026, 11, 5), "Powód");

        var hash = SalesCorrectionInput.ComputeRequestHash(orderId, details);

        Assert.Equal(32, hash.Length);
        Assert.Equal(hash, SalesCorrectionInput.ComputeRequestHash(orderId, details with { Amount = 500.00m }));
        Assert.NotEqual(hash, SalesCorrectionInput.ComputeRequestHash(Guid.NewGuid(), details));
        Assert.NotEqual(hash, SalesCorrectionInput.ComputeRequestHash(orderId, details with { Reason = "Inny" }));
    }

    [Fact]
    public void Audit_uses_sales_correction_type_label_and_parent()
    {
        var correction = TestPayments.NewCorrection();

        var created = PaymentAudit.Created(correction, "ZR-2026-0007", AccountId);
        var deleted = PaymentAudit.Deleted(correction, "ZR-2026-0007", AccountId);

        Assert.Equal(
            new AuditObject(
                AuditObjectType.SalesCorrection,
                correction.Id,
                new DateOnly(2026, 11, 3),
                "Korekta sprzedaży do ZR-2026-0007",
                new AuditObjectReference(AuditObjectType.Order, TestPayments.OrderId)),
            created.Object);
        Assert.Equal(
            [new AuditChange("amount", null, "500.00"), new AuditChange("date", null, "2026-11-03"), new AuditChange("reason", null, "Oddany kamień")],
            created.Changes);
        Assert.Equal(
            [new AuditChange("amount", "500.00", null), new AuditChange("date", "2026-11-03", null), new AuditChange("reason", "Oddany kamień", null)],
            deleted.Changes);
        Assert.Equal(AuditObjectType.Payment, PaymentAudit.ObjectType(TestPayments.NewPayment()));
        Assert.Equal(AuditObjectType.Refund, PaymentAudit.ObjectType(TestPayments.NewRefund()));
    }
}
