using StoneFlow.Expenses.Application;
using StoneFlow.Expenses.Domain.OwnerWithdrawals;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Expenses.Tests.Application;

public sealed class OwnerWithdrawalAuditTests
{
    private static readonly AuditAuthor Author = AuditAuthor.Account(Guid.Parse("4c2f8a3e-1b7d-4e55-9a0f-2d6b1c3e5f70"));

    [Fact]
    public void Created_lists_all_fields_with_codes_and_marks_person_as_personal_data()
    {
        var withdrawal = TestOwnerWithdrawals.Create();

        var entry = OwnerWithdrawalAudit.Created(withdrawal, Author);

        Assert.Equal(new AuditObject(AuditObjectType.OwnerWithdrawal, withdrawal.Id, new DateOnly(2026, 11, 4), "Wypłata właściciela"), entry.Object);
        Assert.Null(entry.Object.Parent);
        Assert.Equal(AuditOperation.Created, entry.Operation);
        Assert.Equal(Author, entry.Author);
        Assert.Equal(
            [
                new AuditChange("person", null, "Właściciel A", IsPersonalData: true),
                new AuditChange("amount", null, "2000.00"),
                new AuditChange("withdrawnOn", null, "2026-11-04"),
                new AuditChange("method", null, "transfer"),
            ],
            entry.Changes);
    }

    [Fact]
    public void Updated_lists_only_changed_fields_and_uses_the_new_withdrawal_date()
    {
        var withdrawal = TestOwnerWithdrawals.Create();
        var changes = withdrawal.Edit(
            TestOwnerWithdrawals.Content() with { Amount = 1500.5m, WithdrawnOn = new DateOnly(2026, 11, 2) }, TestOwnerWithdrawals.CreatedAt);

        var entry = OwnerWithdrawalAudit.Updated(withdrawal, changes, Author);

        Assert.Equal(AuditOperation.Updated, entry.Operation);
        Assert.Equal(new DateOnly(2026, 11, 2), entry.Object.BusinessDate);
        Assert.Equal(
            [new AuditChange("amount", "2000.00", "1500.50"), new AuditChange("withdrawnOn", "2026-11-04", "2026-11-02")],
            entry.Changes);
    }

    [Fact]
    public void Deleted_keeps_all_last_values_as_old_values()
    {
        var withdrawal = TestOwnerWithdrawals.Create(TestOwnerWithdrawals.Content() with { Method = OwnerWithdrawalMethod.Cash });

        var entry = OwnerWithdrawalAudit.Deleted(withdrawal, Author);

        Assert.Equal(AuditOperation.Deleted, entry.Operation);
        Assert.Equal(
            [
                new AuditChange("person", "Właściciel A", null, IsPersonalData: true),
                new AuditChange("amount", "2000.00", null),
                new AuditChange("withdrawnOn", "2026-11-04", null),
                new AuditChange("method", "cash", null),
            ],
            entry.Changes);
    }

    [Fact]
    public void Label_never_contains_the_person()
    {
        var withdrawal = TestOwnerWithdrawals.Create(TestOwnerWithdrawals.Content() with { Person = "Wspólnik B" });

        Assert.DoesNotContain("Wspólnik", OwnerWithdrawalAudit.Created(withdrawal, Author).Object.Label, StringComparison.Ordinal);
        Assert.True(OwnerWithdrawalAudit.IsPersonalData(OwnerWithdrawalField.Person));
        Assert.False(OwnerWithdrawalAudit.IsPersonalData(OwnerWithdrawalField.Amount));
    }
}
