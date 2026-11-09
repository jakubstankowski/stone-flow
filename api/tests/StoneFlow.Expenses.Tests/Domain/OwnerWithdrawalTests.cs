using System.Globalization;
using StoneFlow.Expenses.Domain.Expenses;
using StoneFlow.Expenses.Domain.OwnerWithdrawals;

namespace StoneFlow.Expenses.Tests.Domain;

public sealed class OwnerWithdrawalTests
{
    [Theory]
    [InlineData("0", OwnerWithdrawalAmountError.NotPositive)]
    [InlineData("-0.01", OwnerWithdrawalAmountError.NotPositive)]
    [InlineData("-2000", OwnerWithdrawalAmountError.NotPositive)]
    [InlineData("0.01", OwnerWithdrawalAmountError.None)]
    [InlineData("9999999999999999.99", OwnerWithdrawalAmountError.None)]
    [InlineData("10000000000000000.00", OwnerWithdrawalAmountError.OutOfRange)]
    public void Amount_must_be_positive_and_fit_decimal_18_2(string amount, OwnerWithdrawalAmountError expected)
    {
        Assert.Equal(expected, OwnerWithdrawal.AmountErrorOf(decimal.Parse(amount, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Maximum_is_the_same_as_for_expenses()
    {
        Assert.Equal(OwnerWithdrawalAmountError.None, OwnerWithdrawal.AmountErrorOf(ExpenseAmounts.MaxAbsolute));
    }

    [Fact]
    public void Create_sets_fields_and_equal_timestamps()
    {
        var id = Guid.NewGuid();
        var key = Guid.NewGuid();
        var hash = new byte[32];

        var withdrawal = OwnerWithdrawal.Create(id, TestOwnerWithdrawals.Content(), key, hash, TestOwnerWithdrawals.CreatedAt);

        Assert.Equal(id, withdrawal.Id);
        Assert.Equal("Właściciel A", withdrawal.Person);
        Assert.Equal(2000.00m, withdrawal.Amount);
        Assert.Equal(new DateOnly(2026, 11, 4), withdrawal.WithdrawnOn);
        Assert.Equal(OwnerWithdrawalMethod.Transfer, withdrawal.Method);
        Assert.Equal(key, withdrawal.CreationRequestId);
        Assert.Same(hash, withdrawal.CreationRequestHash);
        Assert.Equal(TestOwnerWithdrawals.CreatedAt, withdrawal.CreatedAtUtc);
        Assert.Equal(withdrawal.CreatedAtUtc, withdrawal.UpdatedAtUtc);
    }

    [Fact]
    public void Create_requires_identifiers()
    {
        Assert.Throws<ArgumentException>(() => OwnerWithdrawal.Create(Guid.Empty, TestOwnerWithdrawals.Content(), Guid.NewGuid(), new byte[32], TestOwnerWithdrawals.CreatedAt));
        Assert.Throws<ArgumentException>(() => OwnerWithdrawal.Create(Guid.NewGuid(), TestOwnerWithdrawals.Content(), Guid.Empty, new byte[32], TestOwnerWithdrawals.CreatedAt));
    }

    [Theory]
    [InlineData("", "2000.00")]
    [InlineData("  ", "2000.00")]
    [InlineData(null, "2000.00")]
    [InlineData("Właściciel A", "0")]
    [InlineData("Właściciel A", "-1.00")]
    public void Create_rejects_content_that_validation_would_reject(string? person, string amount)
    {
        var content = TestOwnerWithdrawals.Content() with
        {
            Person = person!,
            Amount = decimal.Parse(amount, CultureInfo.InvariantCulture),
        };

        Assert.Throws<ArgumentException>(() => TestOwnerWithdrawals.Create(content));
    }

    [Fact]
    public void Create_rejects_a_person_longer_than_100_characters()
    {
        Assert.Throws<ArgumentException>(() => TestOwnerWithdrawals.Create(TestOwnerWithdrawals.Content() with { Person = new string('a', 101) }));
        Assert.Equal(100, TestOwnerWithdrawals.Create(TestOwnerWithdrawals.Content() with { Person = new string('a', 100) }).Person.Length);
    }

    [Fact]
    public void Edit_returns_changed_fields_in_snapshot_order_and_updates_the_timestamp()
    {
        var withdrawal = TestOwnerWithdrawals.Create();
        var later = TestOwnerWithdrawals.CreatedAt.AddHours(2);

        var changes = withdrawal.Edit(
            new OwnerWithdrawalContent("Wspólnik B", 2000.00m, new DateOnly(2026, 11, 3), OwnerWithdrawalMethod.Cash),
            later);

        Assert.Equal(
            [
                new OwnerWithdrawalFieldChange(OwnerWithdrawalField.Person, "Właściciel A", "Wspólnik B"),
                new OwnerWithdrawalFieldChange(OwnerWithdrawalField.WithdrawnOn, new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 3)),
                new OwnerWithdrawalFieldChange(OwnerWithdrawalField.Method, OwnerWithdrawalMethod.Transfer, OwnerWithdrawalMethod.Cash),
            ],
            changes);
        Assert.Equal(later, withdrawal.UpdatedAtUtc);
        Assert.Equal(TestOwnerWithdrawals.CreatedAt, withdrawal.CreatedAtUtc);
    }

    [Fact]
    public void Edit_without_changes_returns_no_fields_and_keeps_the_timestamp()
    {
        var withdrawal = TestOwnerWithdrawals.Create();

        var changes = withdrawal.Edit(TestOwnerWithdrawals.Content() with { Amount = 2000.0m }, TestOwnerWithdrawals.CreatedAt.AddDays(1));

        Assert.Empty(changes);
        Assert.Equal(TestOwnerWithdrawals.CreatedAt, withdrawal.UpdatedAtUtc);
    }

    [Fact]
    public void Edit_rejects_invalid_content()
    {
        var withdrawal = TestOwnerWithdrawals.Create();

        Assert.Throws<ArgumentException>(() => withdrawal.Edit(TestOwnerWithdrawals.Content() with { Amount = 0m }, TestOwnerWithdrawals.CreatedAt));
        Assert.Equal(2000.00m, withdrawal.Amount);
    }
}
