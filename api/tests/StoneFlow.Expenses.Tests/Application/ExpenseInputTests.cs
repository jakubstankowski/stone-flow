using StoneFlow.Expenses.Application;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Domain.Expenses;

namespace StoneFlow.Expenses.Tests.Application;

public sealed class ExpenseInputTests
{
    private static readonly DateOnly Today = TestExpenses.Today;

    [Fact]
    public void Valid_input_is_normalized_into_content()
    {
        var input = TestExpenses.Input(description: "  Cięcie płyt ", counterparty: " ", grossAmount: "-500.00", netAmount: "-406.50", payer: " Właściciel ");

        var errors = new FieldErrors();
        var content = input.Validate(errors, Today);

        Assert.True(errors.IsEmpty);
        Assert.NotNull(content);
        Assert.Equal("Cięcie płyt", content.Description);
        Assert.Null(content.Counterparty);
        Assert.Equal(-500.00m, content.GrossAmount);
        Assert.Equal(-406.50m, content.NetAmount);
        Assert.Equal("Właściciel", content.Payer);
        Assert.Equal(ExpenseCategory.Cutting, content.Category);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("-0")]
    public void Zero_gross_amount_is_rejected(string gross)
    {
        Assert.Equal([ExpenseMessages.GrossZero], ErrorsFor(TestExpenses.Input(grossAmount: gross, netAmount: null), ExpenseFieldKeys.GrossAmount));
    }

    [Theory]
    [InlineData(null, ExpenseMessages.GrossRequired)]
    [InlineData("abc", ExpenseMessages.AmountInvalid)]
    [InlineData("1,50", ExpenseMessages.AmountInvalid)]
    [InlineData("10.005", ExpenseMessages.AmountScale)]
    [InlineData("10000000000000000.00", ExpenseMessages.AmountTooLarge)]
    public void Gross_amount_format_errors_have_specific_messages(string? gross, string message)
    {
        Assert.Equal([message], ErrorsFor(TestExpenses.Input(grossAmount: gross, netAmount: null), ExpenseFieldKeys.GrossAmount));
    }

    [Theory]
    [InlineData("100.00", "0", null)]
    [InlineData("-100.00", "0.00", null)]
    [InlineData("-500.00", "406.50", ExpenseMessages.NetSign)]
    [InlineData("100.00", "120.00", ExpenseMessages.NetExceedsGross)]
    public void Net_amount_zero_is_allowed_and_otherwise_matches_gross(string gross, string net, string? message)
    {
        var errors = ErrorsFor(TestExpenses.Input(grossAmount: gross, netAmount: net), ExpenseFieldKeys.NetAmount);

        Assert.Equal(message is null ? [] : [message], errors);
    }

    [Fact]
    public void Payment_date_after_today_is_rejected_and_today_or_before_cost_date_is_accepted()
    {
        Assert.Equal([ExpenseMessages.PaidOnInFuture], ErrorsFor(TestExpenses.Input(paidOn: "2026-11-06"), ExpenseFieldKeys.PaidOn));
        Assert.Empty(ErrorsFor(TestExpenses.Input(paidOn: "2026-11-05"), ExpenseFieldKeys.PaidOn));
        Assert.Empty(ErrorsFor(TestExpenses.Input(costDate: "2026-10-28", paidOn: "2026-10-01"), ExpenseFieldKeys.PaidOn));
    }

    [Fact]
    public void Cost_date_before_launch_and_in_the_future_is_accepted()
    {
        Assert.Empty(ErrorsFor(TestExpenses.Input(costDate: "2020-01-15", paidOn: "2026-11-05"), ExpenseFieldKeys.CostDate));
        Assert.Empty(ErrorsFor(TestExpenses.Input(costDate: "2027-03-01"), ExpenseFieldKeys.CostDate));
    }

    [Fact]
    public void Required_fields_unknown_codes_and_lengths_are_reported_by_json_key()
    {
        var input = TestExpenses.Input(
            description: " ",
            category: "fuel",
            costDate: "28.10.2026",
            dueDate: "jutro",
            paymentMethod: "card",
            payer: new string('p', 101),
            comment: new string('c', 2001),
            documentReference: new string('d', 101),
            counterparty: new string('k', 201));

        var errors = new FieldErrors();
        Assert.Null(input.Validate(errors, Today));

        Assert.Equal(
            ["category", "comment", "costDate", "counterparty", "description", "documentReference", "dueDate", "payer", "paymentMethod"],
            errors.ToDictionary().Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Mark_paid_requires_a_date_not_after_today_and_a_known_method()
    {
        var missing = new FieldErrors();
        Assert.Null(ExpenseInput.ValidatePaid(null, null, missing, Today));
        Assert.Equal([ExpenseMessages.PaidOnRequired], missing.ToDictionary()[ExpenseFieldKeys.PaidOn]);

        var future = new FieldErrors();
        Assert.Null(ExpenseInput.ValidatePaid("2026-11-06", "cash", future, Today));
        Assert.True(future.Contains(ExpenseFieldKeys.PaidOn));

        var unknown = new FieldErrors();
        Assert.Null(ExpenseInput.ValidatePaid("2026-11-05", "card", unknown, Today));
        Assert.True(unknown.Contains(ExpenseFieldKeys.PaymentMethod));

        Assert.Equal(new ExpensePaidInput(Today, null), ExpenseInput.ValidatePaid("2026-11-05", " ", new FieldErrors(), Today));
    }

    [Fact]
    public void Today_in_Warsaw_switches_at_local_midnight()
    {
        // 5.11.2026 23:30 UTC to już 6.11.2026 w Polsce (CET, UTC+1).
        Assert.Equal(new DateOnly(2026, 11, 6), WarsawTime.Today(new DateTimeOffset(2026, 11, 5, 23, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 11, 5), WarsawTime.Today(new DateTimeOffset(2026, 11, 5, 22, 59, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Request_hash_ignores_whitespace_and_amount_notation_but_not_values()
    {
        var first = Content(TestExpenses.Input(description: "Cięcie płyt", grossAmount: "1234.5"));
        var same = Content(TestExpenses.Input(description: "  Cięcie płyt  ", grossAmount: "1234.50"));
        var other = Content(TestExpenses.Input(grossAmount: "1234.51"));

        Assert.Equal(ExpenseInput.ComputeRequestHash(first), ExpenseInput.ComputeRequestHash(same));
        Assert.NotEqual(ExpenseInput.ComputeRequestHash(first), ExpenseInput.ComputeRequestHash(other));
        Assert.Equal(32, ExpenseInput.ComputeRequestHash(first).Length);
    }

    [Fact]
    public void Version_must_be_eight_bytes_of_base64()
    {
        var errors = new FieldErrors();
        Assert.Null(ExpenseVersion.Validate(null, errors));
        Assert.Null(ExpenseVersion.Validate("abc", errors));
        Assert.NotNull(ExpenseVersion.Validate(Convert.ToBase64String(new byte[8]), new FieldErrors()));
    }

    private static ExpenseContent Content(ExpenseInput input)
    {
        var errors = new FieldErrors();
        return input.Validate(errors, Today) ?? throw new InvalidOperationException(string.Join(", ", errors.ToDictionary().Keys));
    }

    private static string[] ErrorsFor(ExpenseInput input, string key)
    {
        var errors = new FieldErrors();
        input.Validate(errors, Today);
        return errors.ToDictionary().TryGetValue(key, out var messages) ? messages : [];
    }
}
