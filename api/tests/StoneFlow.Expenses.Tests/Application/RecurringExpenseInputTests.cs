using StoneFlow.Expenses.Application;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Application.Recurring;
using StoneFlow.Expenses.Domain.Expenses;

namespace StoneFlow.Expenses.Tests.Application;

public sealed class RecurringExpenseInputTests
{
    private static RecurringExpenseInput Input(
        string? description = "Abonament",
        string? category = "marketing",
        string? counterparty = "Dostawca B",
        string? grossAmount = "300.00",
        string? netAmount = "243.90",
        string? paymentMethod = "transfer",
        string? payer = null,
        string? comment = null,
        int? dayOfMonth = 10,
        string? startsOn = "2026-11-01",
        string? endsOn = null) =>
        new(description, category, counterparty, grossAmount, netAmount, paymentMethod, payer, comment, dayOfMonth, startsOn, endsOn);

    [Fact]
    public void Valid_input_is_normalised()
    {
        var errors = new FieldErrors();

        var input = Input(description: "  Abonament ", counterparty: " ", endsOn: "2027-10-31").Validate(errors);

        Assert.True(errors.IsEmpty);
        Assert.NotNull(input);
        Assert.Equal("Abonament", input.Content.Description);
        Assert.Null(input.Content.Counterparty);
        Assert.Equal(ExpenseCategory.Marketing, input.Content.Category);
        Assert.Equal(300m, input.Content.GrossAmount);
        Assert.Equal(243.90m, input.Content.NetAmount);
        Assert.Equal(new DateOnly(2026, 11, 1), input.Schedule.StartsOn);
        Assert.Equal(new DateOnly(2027, 10, 31), input.Schedule.EndsOn);
        Assert.Equal(10, input.Schedule.DayOfMonth);
    }

    [Theory]
    [InlineData("0.00", RecurringMessages.GrossNotPositive)]
    [InlineData("-10.00", RecurringMessages.GrossNotPositive)]
    [InlineData(null, ExpenseMessages.GrossRequired)]
    [InlineData("1,5", ExpenseMessages.AmountInvalid)]
    [InlineData("1.555", ExpenseMessages.AmountScale)]
    public void Gross_amount_must_be_a_positive_amount(string? gross, string message)
    {
        var errors = new FieldErrors();

        Assert.Null(Input(grossAmount: gross, netAmount: null).Validate(errors));
        Assert.Equal([message], errors.ToDictionary()["grossAmount"]);
    }

    [Theory]
    [InlineData("-1.00", RecurringMessages.NetNegative)]
    [InlineData("300.01", ExpenseMessages.NetExceedsGross)]
    public void Net_amount_is_between_zero_and_gross(string net, string message)
    {
        var errors = new FieldErrors();

        Assert.Null(Input(netAmount: net).Validate(errors));
        Assert.Equal([message], errors.ToDictionary()["netAmount"]);
    }

    [Fact]
    public void Zero_net_is_allowed()
    {
        Assert.NotNull(Input(netAmount: "0.00").Validate(new FieldErrors()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(32)]
    public void Day_of_month_is_from_1_to_31(int? day)
    {
        var errors = new FieldErrors();

        Assert.Null(Input(dayOfMonth: day).Validate(errors));
        Assert.Equal([RecurringMessages.DayOfMonthInvalid], errors.ToDictionary()["dayOfMonth"]);
    }

    [Theory]
    [InlineData(null, RecurringMessages.StartsOnRequired)]
    [InlineData("01.11.2026", RecurringMessages.StartsOnInvalid)]
    [InlineData("1999-12-31", RecurringMessages.StartsOnOutOfRange)]
    [InlineData("2100-01-01", RecurringMessages.StartsOnOutOfRange)]
    public void Start_is_required_in_years_2000_to_2099(string? startsOn, string message)
    {
        var errors = new FieldErrors();

        Assert.Null(Input(startsOn: startsOn).Validate(errors));
        Assert.Equal([message], errors.ToDictionary()["startsOn"]);
    }

    [Theory]
    [InlineData("2026-10-31", RecurringMessages.EndsBeforeStart)]
    [InlineData("2100-01-01", RecurringMessages.EndsOnOutOfRange)]
    [InlineData("31.12.2026", RecurringMessages.EndsOnInvalid)]
    public void End_is_optional_but_not_before_the_start(string endsOn, string message)
    {
        var errors = new FieldErrors();

        Assert.Null(Input(endsOn: endsOn).Validate(errors));
        Assert.Equal([message], errors.ToDictionary()["endsOn"]);
    }

    [Fact]
    public void Text_rules_are_the_same_as_for_an_expense()
    {
        var errors = new FieldErrors();

        Assert.Null(Input(description: " ", category: "unknown", paymentMethod: "card", payer: new string('a', 101)).Validate(errors));

        var dictionary = errors.ToDictionary();
        Assert.Equal([ExpenseMessages.DescriptionRequired], dictionary["description"]);
        Assert.Equal([ExpenseMessages.CategoryUnknown], dictionary["category"]);
        Assert.Equal([ExpenseMessages.PaymentMethodUnknown], dictionary["paymentMethod"]);
        Assert.Equal([ExpenseMessages.PayerTooLong], dictionary["payer"]);
    }

    [Fact]
    public void Request_hash_ignores_normalisation_and_changes_with_the_schedule()
    {
        var hash = RecurringExpenseInput.ComputeRequestHash(Input().Validate(new FieldErrors())!);
        var same = RecurringExpenseInput.ComputeRequestHash(Input(description: " Abonament  ", grossAmount: "300").Validate(new FieldErrors())!);
        var otherDay = RecurringExpenseInput.ComputeRequestHash(Input(dayOfMonth: 11).Validate(new FieldErrors())!);
        var otherEnd = RecurringExpenseInput.ComputeRequestHash(Input(endsOn: "2027-01-31").Validate(new FieldErrors())!);

        Assert.Equal(32, hash.Length);
        Assert.Equal(hash, same);
        Assert.NotEqual(hash, otherDay);
        Assert.NotEqual(hash, otherEnd);
    }
}
