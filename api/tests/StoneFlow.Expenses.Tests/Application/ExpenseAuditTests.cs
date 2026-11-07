using StoneFlow.Expenses.Application;
using StoneFlow.Expenses.Domain.Expenses;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Expenses.Tests.Application;

public sealed class ExpenseAuditTests
{
    private static readonly AuditAuthor Author = AuditAuthor.Account(Guid.Parse("4c2f8a3e-1b7d-4e55-9a0f-2d6b1c3e5f70"));

    [Fact]
    public void Created_lists_non_empty_fields_with_codes_and_marks_payer_as_personal_data()
    {
        var expense = TestExpenses.Create(TestExpenses.Content() with { GrossAmount = -1234.50m, NetAmount = -1003.66m, Payer = "Właściciel" });

        var entry = ExpenseAudit.Created(expense, Author);

        Assert.Equal(new AuditObject(AuditObjectType.Expense, expense.Id, new DateOnly(2026, 10, 28), "Wydatek – Cięcie"), entry.Object);
        Assert.Null(entry.Object.Parent);
        Assert.Equal(AuditOperation.Created, entry.Operation);
        Assert.Equal(
            [
                new AuditChange("description", null, "Cięcie płyt"),
                new AuditChange("category", null, "cutting"),
                new AuditChange("counterparty", null, "Dostawca A"),
                new AuditChange("costDate", null, "2026-10-28"),
                new AuditChange("dueDate", null, "2026-11-10"),
                new AuditChange("grossAmount", null, "-1234.50"),
                new AuditChange("netAmount", null, "-1003.66"),
                new AuditChange("documentReference", null, "FV 12/2026"),
                new AuditChange("paymentMethod", null, "transfer"),
                new AuditChange("payer", null, "Właściciel", IsPersonalData: true),
            ],
            entry.Changes);
    }

    [Fact]
    public void Updated_lists_only_changed_fields_and_uses_the_new_cost_date()
    {
        var expense = TestExpenses.Create();
        var changes = expense.Edit(TestExpenses.Content() with { CostDate = new DateOnly(2026, 11, 2), PaidOn = new DateOnly(2026, 11, 3) }, TestExpenses.CreatedAt).Changes;

        var entry = ExpenseAudit.Updated(expense, changes, Author);

        Assert.Equal(new DateOnly(2026, 11, 2), entry.Object.BusinessDate);
        Assert.Equal(
            [new AuditChange("costDate", "2026-10-28", "2026-11-02"), new AuditChange("paidOn", null, "2026-11-03")],
            entry.Changes);
    }

    [Fact]
    public void Deleted_keeps_all_last_values_as_old_values()
    {
        var expense = TestExpenses.Create(TestExpenses.Content() with { NetAmount = 0m });

        var entry = ExpenseAudit.Deleted(expense, Author);

        Assert.Equal(AuditOperation.Deleted, entry.Operation);
        Assert.All(entry.Changes, change => Assert.Null(change.NewValue));
        Assert.Contains(new AuditChange("netAmount", "0.00", null), entry.Changes);
        Assert.Equal(9, entry.Changes.Count);
    }

    [Fact]
    public void Optional_parent_is_passed_through_for_recurring_occurrences()
    {
        var parent = new AuditObjectReference(AuditObjectType.RecurringExpenseDefinition, Guid.NewGuid());

        var entry = ExpenseAudit.Created(TestExpenses.Create(), Author, parent);

        Assert.Equal(parent, entry.Object.Parent);
    }

    [Fact]
    public void Label_contains_only_the_category_label()
    {
        var expense = TestExpenses.Create(TestExpenses.Content() with { Category = ExpenseCategory.PpeTax, Description = "Jan Kowalski" });

        Assert.Equal("Wydatek – Podatek PPE", ExpenseAudit.Label(expense));
    }
}
