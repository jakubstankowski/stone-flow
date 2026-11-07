using StoneFlow.Expenses.Domain.Expenses;

namespace StoneFlow.Expenses.Tests.Domain;

public sealed class ExpenseTests
{
    private static readonly DateOnly Today = TestExpenses.Today;
    private static readonly DateTime Later = TestExpenses.CreatedAt.AddHours(1);

    [Fact]
    public void Without_payment_date_the_expense_is_planned_and_with_it_paid()
    {
        Assert.Equal(new ExpensePaymentInfo(ExpensePaymentStatus.Planned, false), ExpensePayment.Compute(null, null, Today));
        Assert.Equal(new ExpensePaymentInfo(ExpensePaymentStatus.Paid, false), ExpensePayment.Compute(Today, null, Today));
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void Planned_expense_is_overdue_only_after_the_due_date(int dueOffset, bool overdue)
    {
        var info = ExpensePayment.Compute(null, Today.AddDays(dueOffset), Today);

        Assert.Equal(ExpensePaymentStatus.Planned, info.Status);
        Assert.Equal(overdue, info.Overdue);
    }

    [Fact]
    public void Paid_expense_is_never_overdue()
    {
        Assert.False(ExpensePayment.Compute(Today.AddDays(-30), Today.AddDays(-20), Today).Overdue);
    }

    [Theory]
    [InlineData("0", null, ExpenseAmountError.GrossZero)]
    [InlineData("100.00", null, ExpenseAmountError.None)]
    [InlineData("-100.00", null, ExpenseAmountError.None)]
    [InlineData("100.00", "0", ExpenseAmountError.None)]
    [InlineData("-100.00", "0", ExpenseAmountError.None)]
    [InlineData("100.00", "81.30", ExpenseAmountError.None)]
    [InlineData("-500.00", "-406.50", ExpenseAmountError.None)]
    [InlineData("100.00", "100.00", ExpenseAmountError.None)]
    [InlineData("-500.00", "406.50", ExpenseAmountError.NetSign)]
    [InlineData("100.00", "-1.00", ExpenseAmountError.NetSign)]
    [InlineData("100.00", "120.00", ExpenseAmountError.NetExceedsGross)]
    [InlineData("-100.00", "-100.01", ExpenseAmountError.NetExceedsGross)]
    public void Amount_rules_allow_zero_net_and_require_matching_sign_and_magnitude(string gross, string? net, ExpenseAmountError expected)
    {
        Assert.Equal(expected, ExpenseAmounts.Check(decimal.Parse(gross, System.Globalization.CultureInfo.InvariantCulture), net is null ? null : decimal.Parse(net, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Amount_range_is_decimal_18_2_in_both_directions()
    {
        Assert.Equal(ExpenseAmountError.None, ExpenseAmounts.Check(ExpenseAmounts.MaxAbsolute, null));
        Assert.Equal(ExpenseAmountError.None, ExpenseAmounts.Check(-ExpenseAmounts.MaxAbsolute, null));
        Assert.Equal(ExpenseAmountError.GrossOutOfRange, ExpenseAmounts.Check(ExpenseAmounts.MaxAbsolute + 0.01m, null));
    }

    [Fact]
    public void Create_rejects_zero_gross_amount()
    {
        Assert.Throws<ArgumentException>(() => TestExpenses.Create(TestExpenses.Content() with { GrossAmount = 0m, NetAmount = null }));
    }

    [Fact]
    public void Edit_returns_changed_fields_in_snapshot_order_and_touches_update_time()
    {
        var expense = TestExpenses.Create();

        var result = expense.Edit(TestExpenses.Content() with { Comment = "Poprawka", GrossAmount = 1300m, NetAmount = null }, Later);

        Assert.True(result.Succeeded);
        Assert.Equal(
            [
                new ExpenseFieldChange(ExpenseField.GrossAmount, 1234.50m, 1300m),
                new ExpenseFieldChange(ExpenseField.NetAmount, 1003.66m, null),
                new ExpenseFieldChange(ExpenseField.Comment, null, "Poprawka"),
            ],
            result.Changes);
        Assert.Equal(Later, expense.UpdatedAtUtc);
    }

    [Fact]
    public void Edit_without_changes_returns_no_changes_and_keeps_update_time()
    {
        var expense = TestExpenses.Create();

        var result = expense.Edit(TestExpenses.Content(), Later);

        Assert.Empty(result.Changes);
        Assert.Equal(TestExpenses.CreatedAt, expense.UpdatedAtUtc);
    }

    [Fact]
    public void Mark_paid_sets_date_and_replaces_method_and_cannot_be_repeated()
    {
        var expense = TestExpenses.Create();

        var marked = expense.MarkPaid(Today, ExpensePaymentMethod.Blik, Later);
        var again = expense.MarkPaid(Today, null, Later);

        Assert.Equal(
            [
                new ExpenseFieldChange(ExpenseField.PaidOn, null, Today),
                new ExpenseFieldChange(ExpenseField.PaymentMethod, ExpensePaymentMethod.Transfer, ExpensePaymentMethod.Blik),
            ],
            marked.Changes);
        Assert.True(expense.IsPaid);
        Assert.Equal(ExpenseRuleViolation.AlreadyPaid, again.Violation);
        Assert.Equal(ExpensePaymentMethod.Blik, expense.PaymentMethod);
    }

    [Fact]
    public void Unmark_paid_clears_only_the_payment_date_and_requires_a_paid_expense()
    {
        var planned = TestExpenses.Create();
        Assert.Equal(ExpenseRuleViolation.NotPaid, planned.UnmarkPaid(Later).Violation);

        var paid = TestExpenses.Create(TestExpenses.Content() with { PaidOn = Today });
        var result = paid.UnmarkPaid(Later);

        Assert.Equal([new ExpenseFieldChange(ExpenseField.PaidOn, Today, null)], result.Changes);
        Assert.False(paid.IsPaid);
        Assert.Equal(ExpensePaymentMethod.Transfer, paid.PaymentMethod);
    }
}
