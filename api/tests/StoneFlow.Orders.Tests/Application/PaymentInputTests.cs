using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Application.Payments;
using StoneFlow.Orders.Domain.Payments;

namespace StoneFlow.Orders.Tests.Application;

public sealed class PaymentInputTests
{
    /// <summary>5.11.2026 12:00 UTC (13:00 w Warszawie).</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid OrderId = Guid.Parse("0b8c6d2e-5f41-4a7b-9c3d-1e2f3a4b5c6d");

    private static (PaymentDetails? Details, IDictionary<string, string[]> Errors) Validate(PaymentInput input, DateTimeOffset? now = null)
    {
        var errors = new FieldErrors();
        var details = input.Validate(errors, now ?? Now);
        return (details, errors.ToDictionary());
    }

    private static (RefundDetails? Details, IDictionary<string, string[]> Errors) Validate(RefundInput input, DateTimeOffset? now = null)
    {
        var errors = new FieldErrors();
        var details = input.Validate(errors, now ?? Now);
        return (details, errors.ToDictionary());
    }

    [Fact]
    public void Valid_payment_is_normalized()
    {
        var (details, errors) = Validate(new PaymentInput(" 300.5 ", "2026-11-05", "cash", "  Anna Przykładowa "));

        Assert.Empty(errors);
        Assert.Equal(new PaymentDetails(300.5m, new DateOnly(2026, 11, 5), PaymentMethod.Cash, "Anna Przykładowa"), details);
    }

    [Fact]
    public void Empty_received_by_is_null_for_any_method()
    {
        var (details, errors) = Validate(new PaymentInput("10", "2026-11-01", "cashOnDelivery", "  "));

        Assert.Empty(errors);
        Assert.Null(details!.ReceivedBy);
        Assert.Equal(PaymentMethod.CashOnDelivery, details.Method);
    }

    [Theory]
    [InlineData(null, EntryMessages.AmountRequired)]
    [InlineData("0", EntryMessages.AmountNotPositive)]
    [InlineData("0.00", EntryMessages.AmountNotPositive)]
    [InlineData("-5", EntryMessages.AmountNotPositive)]
    [InlineData("1,50", EntryMessages.AmountInvalid)]
    [InlineData("1.234", EntryMessages.AmountScale)]
    [InlineData("10000000000000000", EntryMessages.AmountTooLarge)]
    public void Amount_must_be_positive_with_two_decimals_in_range(string? amount, string message)
    {
        var (details, errors) = Validate(new PaymentInput(amount, "2026-11-05", "transfer", null));
        var (refund, refundErrors) = Validate(new RefundInput("moneyReturn", amount, "2026-11-05", "transfer", "Powód"));

        Assert.Null(details);
        Assert.Equal([message], errors[EntryFieldKeys.Amount]);
        Assert.Null(refund);
        Assert.Equal([message], refundErrors[EntryFieldKeys.Amount]);
    }

    [Theory]
    [InlineData(null, EntryMessages.DateRequired)]
    [InlineData("05.11.2026", EntryMessages.DateInvalid)]
    [InlineData("2026-11-06", EntryMessages.DateInFuture)]
    public void Date_is_required_and_not_in_the_future(string? date, string message)
    {
        var (_, errors) = Validate(new PaymentInput("10", date, "transfer", null));

        Assert.Equal([message], errors[EntryFieldKeys.Date]);
    }

    [Fact]
    public void Old_date_is_accepted()
    {
        var (details, errors) = Validate(new PaymentInput("10", "2020-01-01", "blik", null));

        Assert.Empty(errors);
        Assert.Equal(new DateOnly(2020, 1, 1), details!.Date);
    }

    [Fact]
    public void Today_follows_Europe_Warsaw_time()
    {
        // 5.11.2026 23:30 UTC to już 6.11 w Warszawie (UTC+1).
        var lateEvening = new DateTimeOffset(2026, 11, 5, 23, 30, 0, TimeSpan.Zero);

        var (warsawToday, errors) = Validate(new PaymentInput("10", "2026-11-06", "transfer", null), lateEvening);
        var (_, tomorrowErrors) = Validate(new PaymentInput("10", "2026-11-07", "transfer", null), lateEvening);
        var (_, beforeMidnightErrors) = Validate(new PaymentInput("10", "2026-11-06", "transfer", null), lateEvening.AddHours(-1));

        Assert.Empty(errors);
        Assert.Equal(new DateOnly(2026, 11, 6), warsawToday!.Date);
        Assert.Equal([EntryMessages.DateInFuture], tomorrowErrors[EntryFieldKeys.Date]);
        Assert.Equal([EntryMessages.DateInFuture], beforeMidnightErrors[EntryFieldKeys.Date]);
    }

    [Theory]
    [InlineData(null, EntryMessages.PaymentMethodRequired)]
    [InlineData("card", EntryMessages.PaymentMethodUnknown)]
    [InlineData("Cash", EntryMessages.PaymentMethodUnknown)]
    public void Payment_method_must_be_known(string? method, string message)
    {
        var (_, errors) = Validate(new PaymentInput("10", "2026-11-05", method, null));

        Assert.Equal([message], errors[EntryFieldKeys.Method]);
    }

    [Fact]
    public void Cash_on_delivery_is_not_a_refund_method()
    {
        var (_, errors) = Validate(new RefundInput("moneyReturn", "10", "2026-11-05", "cashOnDelivery", "Powód"));

        Assert.Equal([EntryMessages.RefundMethodUnknown], errors[EntryFieldKeys.Method]);
    }

    [Fact]
    public void Received_by_only_for_cash_and_at_most_100_characters()
    {
        var (_, transfer) = Validate(new PaymentInput("10", "2026-11-05", "transfer", "Anna"));
        var (_, tooLong) = Validate(new PaymentInput("10", "2026-11-05", "cash", new string('a', 101)));
        var (exact, exactErrors) = Validate(new PaymentInput("10", "2026-11-05", "cash", new string('a', 100)));

        Assert.Equal([EntryMessages.ReceivedByOnlyForCash], transfer[EntryFieldKeys.ReceivedBy]);
        Assert.Equal([EntryMessages.ReceivedByTooLong], tooLong[EntryFieldKeys.ReceivedBy]);
        Assert.Empty(exactErrors);
        Assert.Equal(100, exact!.ReceivedBy!.Length);
    }

    [Fact]
    public void Refund_requires_kind_and_reason()
    {
        var (details, errors) = Validate(new RefundInput(null, "10", "2026-11-05", "cash", "   "));
        var (_, unknown) = Validate(new RefundInput("partial", "10", "2026-11-05", "cash", new string('p', 501)));

        Assert.Null(details);
        Assert.Equal([EntryFieldKeys.Kind, EntryFieldKeys.Reason], errors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal([EntryMessages.KindRequired], errors[EntryFieldKeys.Kind]);
        Assert.Equal([EntryMessages.KindUnknown], unknown[EntryFieldKeys.Kind]);
        Assert.Equal([EntryMessages.ReasonTooLong], unknown[EntryFieldKeys.Reason]);
    }

    [Fact]
    public void Valid_refund_is_normalized()
    {
        var (details, errors) = Validate(new RefundInput("salesReturn", "200", "2026-11-04", "blik", "  Oddany towar "));

        Assert.Empty(errors);
        Assert.Equal(new RefundDetails(RefundKind.SalesReturn, 200m, new DateOnly(2026, 11, 4), RefundMethod.Blik, "Oddany towar"), details);
    }

    [Fact]
    public void Missing_fields_are_reported_under_json_paths()
    {
        var (_, payment) = Validate(new PaymentInput(null, null, null, null));
        var (_, refund) = Validate(new RefundInput(null, null, null, null, null));

        Assert.Equal(["amount", "date", "method"], payment.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["amount", "date", "kind", "method", "reason"], refund.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Request_hash_uses_canonical_values_and_includes_order()
    {
        var (first, _) = Validate(new PaymentInput("300.5", "2026-11-05", "cash", " Anna "));
        var (same, _) = Validate(new PaymentInput(" 300.50", "2026-11-05", "cash", "Anna"));
        var (other, _) = Validate(new PaymentInput("300.51", "2026-11-05", "cash", "Anna"));

        var hash = PaymentInput.ComputeRequestHash(OrderId, first!);

        Assert.Equal(32, hash.Length);
        Assert.Equal(hash, PaymentInput.ComputeRequestHash(OrderId, same!));
        Assert.NotEqual(hash, PaymentInput.ComputeRequestHash(OrderId, other!));
        Assert.NotEqual(hash, PaymentInput.ComputeRequestHash(Guid.NewGuid(), first!));

        var refund = new RefundDetails(RefundKind.MoneyReturn, 10m, new DateOnly(2026, 11, 5), RefundMethod.Cash, "Powód");
        Assert.Equal(RefundInput.ComputeRequestHash(OrderId, refund), RefundInput.ComputeRequestHash(OrderId, refund with { Amount = 10.00m }));
        Assert.NotEqual(RefundInput.ComputeRequestHash(OrderId, refund), RefundInput.ComputeRequestHash(Guid.NewGuid(), refund));
        Assert.NotEqual(RefundInput.ComputeRequestHash(OrderId, refund), RefundInput.ComputeRequestHash(OrderId, refund with { Kind = RefundKind.SalesReturn }));
    }
}
