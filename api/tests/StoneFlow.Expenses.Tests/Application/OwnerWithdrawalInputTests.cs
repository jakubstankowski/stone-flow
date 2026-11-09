using StoneFlow.Expenses.Application;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Domain.OwnerWithdrawals;

namespace StoneFlow.Expenses.Tests.Application;

public sealed class OwnerWithdrawalInputTests
{
    private static readonly DateOnly Today = TestOwnerWithdrawals.Today;

    [Fact]
    public void Valid_input_is_normalized_into_content()
    {
        var errors = new FieldErrors();

        var content = TestOwnerWithdrawals.Input(person: "  Właściciel A ", amount: " 2000.5 ", method: "blik").Validate(errors, Today);

        Assert.True(errors.IsEmpty);
        Assert.Equal(new OwnerWithdrawalContent("Właściciel A", 2000.50m, new DateOnly(2026, 11, 4), OwnerWithdrawalMethod.Blik), content);
    }

    [Theory]
    [InlineData(null, OwnerWithdrawalMessages.PersonRequired)]
    [InlineData("   ", OwnerWithdrawalMessages.PersonRequired)]
    public void Person_is_required(string? person, string message)
    {
        Assert.Equal([message], ErrorsFor(TestOwnerWithdrawals.Input(person: person), OwnerWithdrawalFieldKeys.Person));
    }

    [Fact]
    public void Person_is_at_most_100_characters_after_trimming()
    {
        Assert.Empty(ErrorsFor(TestOwnerWithdrawals.Input(person: " " + new string('a', 100) + " "), OwnerWithdrawalFieldKeys.Person));
        Assert.Equal(
            [OwnerWithdrawalMessages.PersonTooLong],
            ErrorsFor(TestOwnerWithdrawals.Input(person: new string('a', 101)), OwnerWithdrawalFieldKeys.Person));
    }

    [Theory]
    [InlineData(null, OwnerWithdrawalMessages.AmountRequired)]
    [InlineData("", OwnerWithdrawalMessages.AmountRequired)]
    [InlineData("abc", OwnerWithdrawalMessages.AmountInvalid)]
    [InlineData("1,50", OwnerWithdrawalMessages.AmountInvalid)]
    [InlineData("1.234", OwnerWithdrawalMessages.AmountScale)]
    [InlineData("10000000000000000", OwnerWithdrawalMessages.AmountTooLarge)]
    [InlineData("0", OwnerWithdrawalMessages.AmountNotPositive)]
    [InlineData("0.00", OwnerWithdrawalMessages.AmountNotPositive)]
    [InlineData("-1", OwnerWithdrawalMessages.AmountNotPositive)]
    public void Amount_errors_have_specific_messages(string? amount, string message)
    {
        Assert.Equal([message], ErrorsFor(TestOwnerWithdrawals.Input(amount: amount), OwnerWithdrawalFieldKeys.Amount));
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("9999999999999999.99")]
    public void Amount_boundaries_are_accepted(string amount)
    {
        Assert.Empty(ErrorsFor(TestOwnerWithdrawals.Input(amount: amount), OwnerWithdrawalFieldKeys.Amount));
    }

    [Theory]
    [InlineData(null, OwnerWithdrawalMessages.WithdrawnOnRequired)]
    [InlineData("04.11.2026", OwnerWithdrawalMessages.WithdrawnOnInvalid)]
    [InlineData("2026-02-30", OwnerWithdrawalMessages.WithdrawnOnInvalid)]
    [InlineData("2026-11-06", OwnerWithdrawalMessages.WithdrawnOnInFuture)]
    public void Withdrawal_date_is_required_valid_and_not_in_the_future(string? date, string message)
    {
        Assert.Equal([message], ErrorsFor(TestOwnerWithdrawals.Input(withdrawnOn: date), OwnerWithdrawalFieldKeys.WithdrawnOn));
    }

    [Theory]
    [InlineData("2026-11-05")]
    [InlineData("2020-01-15")]
    public void Today_and_dates_before_launch_are_accepted(string date)
    {
        Assert.Empty(ErrorsFor(TestOwnerWithdrawals.Input(withdrawnOn: date), OwnerWithdrawalFieldKeys.WithdrawnOn));
    }

    [Fact]
    public void Today_is_judged_in_Warsaw_time()
    {
        // 5.11.2026 23:30 UTC to już 6.11.2026 w Polsce (zima, UTC+1).
        var today = WarsawTime.Today(new DateTimeOffset(2026, 11, 5, 23, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 11, 6), today);
        Assert.Empty(ErrorsFor(TestOwnerWithdrawals.Input(withdrawnOn: "2026-11-06"), OwnerWithdrawalFieldKeys.WithdrawnOn, today));
        Assert.Equal(
            [OwnerWithdrawalMessages.WithdrawnOnInFuture],
            ErrorsFor(TestOwnerWithdrawals.Input(withdrawnOn: "2026-11-07"), OwnerWithdrawalFieldKeys.WithdrawnOn, today));
    }

    [Theory]
    [InlineData(null, OwnerWithdrawalMessages.MethodRequired)]
    [InlineData("cashOnDelivery", OwnerWithdrawalMessages.MethodUnknown)]
    [InlineData("Transfer", OwnerWithdrawalMessages.MethodUnknown)]
    public void Method_is_required_and_from_the_dictionary(string? method, string message)
    {
        Assert.Equal([message], ErrorsFor(TestOwnerWithdrawals.Input(method: method), OwnerWithdrawalFieldKeys.Method));
    }

    [Theory]
    [InlineData("transfer", OwnerWithdrawalMethod.Transfer)]
    [InlineData("blik", OwnerWithdrawalMethod.Blik)]
    [InlineData("cash", OwnerWithdrawalMethod.Cash)]
    public void Method_codes_round_trip(string code, OwnerWithdrawalMethod method)
    {
        Assert.True(OwnerWithdrawalCodes.TryParse(code, out var parsed));
        Assert.Equal(method, parsed);
        Assert.Equal(code, OwnerWithdrawalCodes.Of(method));
    }

    [Fact]
    public void All_errors_are_reported_under_json_keys()
    {
        var errors = new FieldErrors();

        var content = new OwnerWithdrawalInput(null, null, null, null).Validate(errors, Today);

        Assert.Null(content);
        Assert.Equal(["amount", "method", "person", "withdrawnOn"], errors.ToDictionary().Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Request_hash_does_not_depend_on_whitespace_or_amount_notation()
    {
        var first = Content(TestOwnerWithdrawals.Input(person: "Właściciel A", amount: "2000.00"));
        var second = Content(TestOwnerWithdrawals.Input(person: "  Właściciel A ", amount: "2000"));
        var other = Content(TestOwnerWithdrawals.Input(amount: "2000.01"));

        Assert.Equal(OwnerWithdrawalInput.ComputeRequestHash(first), OwnerWithdrawalInput.ComputeRequestHash(second));
        Assert.NotEqual(OwnerWithdrawalInput.ComputeRequestHash(first), OwnerWithdrawalInput.ComputeRequestHash(other));
        Assert.Equal(32, OwnerWithdrawalInput.ComputeRequestHash(first).Length);
    }

    [Theory]
    [InlineData(null, OwnerWithdrawalMessages.VersionRequired)]
    [InlineData("nie-wersja", OwnerWithdrawalMessages.VersionInvalid)]
    public void Version_is_required_and_must_be_a_rowversion(string? version, string message)
    {
        var errors = new FieldErrors();

        Assert.Null(OwnerWithdrawalVersion.Validate(version, errors));
        Assert.Equal([message], errors.ToDictionary()[OwnerWithdrawalFieldKeys.Version]);
        Assert.NotNull(OwnerWithdrawalVersion.Validate(Convert.ToBase64String(new byte[8]), new FieldErrors()));
    }

    private static OwnerWithdrawalContent Content(OwnerWithdrawalInput input)
    {
        var errors = new FieldErrors();
        var content = input.Validate(errors, Today);
        Assert.True(errors.IsEmpty);
        return content!;
    }

    private static string[] ErrorsFor(OwnerWithdrawalInput input, string key, DateOnly? today = null)
    {
        var errors = new FieldErrors();
        input.Validate(errors, today ?? Today);
        return errors.ToDictionary().TryGetValue(key, out var messages) ? messages : [];
    }
}
