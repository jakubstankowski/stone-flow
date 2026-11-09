using StoneFlow.Expenses.Application;
using StoneFlow.Expenses.Application.Recurring;
using StoneFlow.Expenses.Domain.Expenses;
using StoneFlow.Expenses.Domain.Recurring;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Expenses.Tests.Application;

public sealed class RecurringExpenseAuditTests
{
    private static readonly AuditAuthor Author = AuditAuthor.Account(Guid.Parse("7a1e9c42-5d3b-4f80-b6a2-0c9e8d7f6a51"));
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 11, 20);

    private static RecurringExpenseDefinition Definition(string? payer = "Właściciel") =>
        RecurringExpenseDefinition.Create(
            Guid.NewGuid(),
            new RecurringContent("Abonament", ExpenseCategory.Marketing, "Dostawca B", 300m, 243.90m, ExpensePaymentMethod.Transfer, payer, null),
            new RecurringScheduleInput(10, new DateOnly(2026, 11, 1), new DateOnly(2027, 10, 31)),
            Now);

    [Fact]
    public void Created_lists_non_empty_fields_with_the_start_as_business_date_and_payer_as_personal_data()
    {
        var definition = Definition();

        var entry = RecurringExpenseAudit.Created(definition, Author);

        Assert.Equal(
            new AuditObject(AuditObjectType.RecurringExpenseDefinition, definition.Id, new DateOnly(2026, 11, 1), "Wydatek cykliczny – Marketing"),
            entry.Object);
        Assert.Null(entry.Object.Parent);
        Assert.Equal(AuditOperation.Created, entry.Operation);
        Assert.Equal(
            [
                new AuditChange("description", null, "Abonament"),
                new AuditChange("category", null, "marketing"),
                new AuditChange("counterparty", null, "Dostawca B"),
                new AuditChange("grossAmount", null, "300.00"),
                new AuditChange("netAmount", null, "243.90"),
                new AuditChange("paymentMethod", null, "transfer"),
                new AuditChange("payer", null, "Właściciel", IsPersonalData: true),
                new AuditChange("dayOfMonth", null, "10"),
                new AuditChange("startsOn", null, "2026-11-01"),
                new AuditChange("endsOn", null, "2027-10-31"),
            ],
            entry.Changes);
    }

    [Fact]
    public void Day_change_pause_and_resume_are_updates_of_day_of_month_and_paused_on()
    {
        var definition = Definition();
        var registry = new Dictionary<RecurringPeriod, DateOnly>();
        var content = new RecurringContent("Abonament", ExpenseCategory.Marketing, "Dostawca B", 300m, 243.90m, ExpensePaymentMethod.Transfer, "Właściciel", null);

        var day = definition.Edit(content, new RecurringScheduleInput(25, new DateOnly(2026, 11, 1), new DateOnly(2027, 10, 31)), Today, registry, Now);
        var pause = definition.Pause(Today, Now);
        var resume = definition.Resume(new DateOnly(2026, 11, 25), Now);

        Assert.Equal([new AuditChange("dayOfMonth", "10", "25")], RecurringExpenseAudit.Updated(definition, day.Changes, Author).Changes);
        Assert.Equal([new AuditChange("pausedOn", null, "2026-11-20")], RecurringExpenseAudit.Updated(definition, pause.Changes, Author).Changes);
        var resumed = RecurringExpenseAudit.Updated(definition, resume.Changes, Author);
        Assert.Equal([new AuditChange("pausedOn", "2026-11-20", null)], resumed.Changes);
        Assert.Equal(AuditOperation.Updated, resumed.Operation);
    }

    [Fact]
    public void Deleted_lists_all_non_empty_fields_as_old_values()
    {
        var definition = Definition(payer: null);

        var entry = RecurringExpenseAudit.Deleted(definition, Author);

        Assert.Equal(AuditOperation.Deleted, entry.Operation);
        Assert.All(entry.Changes, change => Assert.Null(change.NewValue));
        Assert.DoesNotContain(entry.Changes, change => change.Field == "payer");
        Assert.Contains(new AuditChange("dayOfMonth", "10", null), entry.Changes);
    }

    [Fact]
    public void Occurrence_audit_has_the_definition_as_parent_and_the_due_date_as_business_date()
    {
        var definition = Definition();
        var occurrence = definition.CreateOccurrence(Guid.NewGuid(), new RecurringPeriod(2026, 11), Now);

        var entry = ExpenseAudit.Created(occurrence.Expense, AuditAuthor.AutomatedJob, ExpenseAudit.ParentOf(occurrence.Expense));

        Assert.Equal(new AuditObjectReference(AuditObjectType.RecurringExpenseDefinition, definition.Id), entry.Object.Parent);
        Assert.Equal(new DateOnly(2026, 11, 10), entry.Object.BusinessDate);
        Assert.Equal(AuditAuthorKind.AutomatedJob, entry.Author.Kind);
        Assert.Null(ExpenseAudit.ParentOf(TestExpenses.Create()));
    }
}
