using StoneFlow.Reporting.Domain.Audit;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Reporting.Tests.Audit;

public sealed class AuditRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 13, 30, 0, TimeSpan.FromHours(1));
    private static readonly Guid AccountId = Guid.Parse("4c2f8a3e-1b7d-4e55-9a0f-2d6b1c3e5f70");
    private static readonly Guid OrderId = Guid.Parse("8f1e2d3c-4b5a-4968-8776-655443322110");
    private static readonly DateOnly OrderDate = new(2026, 10, 28);

    private static readonly AuditFieldChange[] AmountChange = [new("amount", "100.00", "120.50", false)];

    [Fact]
    public void Creates_entry_with_author_utc_time_object_operation_and_changes()
    {
        var record = Create(changes: [new("amount", "100.00", "120.50", false), new("customerName", "Jan K.", "Jan Kowalski", true)]);

        Assert.Equal(new DateTime(2026, 11, 4, 12, 30, 0, DateTimeKind.Utc), record.OccurredAtUtc);
        Assert.Equal(AuditAuthorKind.Account, record.AuthorKind);
        Assert.Equal(AccountId, record.AuthorAccountId);
        Assert.Equal(AuditObjectType.Order, record.ObjectType);
        Assert.Equal(OrderId, record.ObjectId);
        Assert.Equal(OrderDate, record.ObjectBusinessDate);
        Assert.Equal("ZAM/2026/0042", record.ObjectLabel);
        Assert.Equal(AuditOperation.Updated, record.Operation);
        Assert.Equal(2, record.Changes.Count);
        Assert.True(record.Changes[1].IsPersonalData);
    }

    [Theory]
    [InlineData(AuditAuthorKind.WooCommerceSync)]
    [InlineData(AuditAuthorKind.AutomatedJob)]
    public void System_author_has_no_account(AuditAuthorKind kind)
    {
        var record = Create(authorKind: kind, accountId: null);

        Assert.Equal(kind, record.AuthorKind);
        Assert.Null(record.AuthorAccountId);
    }

    [Fact]
    public void Account_author_requires_account_id()
    {
        Assert.Throws<ArgumentException>(() => Create(accountId: null, defaultAccount: false));
        Assert.Throws<ArgumentException>(() => Create(accountId: Guid.Empty));
    }

    [Fact]
    public void System_author_cannot_carry_account_id() =>
        Assert.Throws<ArgumentException>(() => Create(authorKind: AuditAuthorKind.AutomatedJob, accountId: AccountId));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Label_is_required(string label) =>
        Assert.Throws<ArgumentException>(() => Create(label: label));

    [Fact]
    public void Label_is_limited_to_200_characters()
    {
        Assert.Equal(200, Create(label: new string('A', 200)).ObjectLabel.Length);
        Assert.Throws<ArgumentException>(() => Create(label: new string('A', 201)));
    }

    [Fact]
    public void Object_id_is_required() =>
        Assert.Throws<ArgumentException>(() => Create(objectId: Guid.Empty));

    [Fact]
    public void Changes_are_required() =>
        Assert.Throws<ArgumentException>(() => Create(changes: []));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Field_name_is_required(string field) =>
        Assert.Throws<ArgumentException>(() => Create(changes: [new(field, null, "1", false)]));

    [Fact]
    public void Deletion_keeps_last_values_as_trace()
    {
        var record = Create(operation: AuditOperation.Deleted, changes: [new("amount", "120.50", null, false)]);

        Assert.Equal(AuditOperation.Deleted, record.Operation);
        Assert.Equal("120.50", Assert.Single(record.Changes).OldValue);
    }

    [Theory]
    [InlineData(AuditObjectType.Payment)]
    [InlineData(AuditObjectType.Refund)]
    [InlineData(AuditObjectType.SalesCorrection)]
    public void Order_children_require_parent_order(AuditObjectType type)
    {
        Assert.Throws<ArgumentException>(() => Create(objectType: type));
        Assert.Throws<ArgumentException>(() => Create(objectType: type, parentType: AuditObjectType.Expense, parentId: OrderId));

        var record = Create(objectType: type, parentType: AuditObjectType.Order, parentId: OrderId);
        Assert.Equal(AuditObjectType.Order, record.ParentObjectType);
        Assert.Equal(OrderId, record.ParentObjectId);
    }

    [Fact]
    public void Expense_may_point_to_recurring_definition()
    {
        Assert.Null(Create(objectType: AuditObjectType.Expense).ParentObjectType);
        Assert.Equal(
            AuditObjectType.RecurringExpenseDefinition,
            Create(objectType: AuditObjectType.Expense, parentType: AuditObjectType.RecurringExpenseDefinition, parentId: OrderId).ParentObjectType);
        Assert.Throws<ArgumentException>(() => Create(objectType: AuditObjectType.Expense, parentType: AuditObjectType.Order, parentId: OrderId));
    }

    [Theory]
    [InlineData(AuditObjectType.Order)]
    [InlineData(AuditObjectType.RecurringExpenseDefinition)]
    [InlineData(AuditObjectType.OwnerWithdrawal)]
    public void Other_objects_have_no_parent(AuditObjectType type) =>
        Assert.Throws<ArgumentException>(() => Create(objectType: type, parentType: AuditObjectType.Order, parentId: OrderId));

    [Fact]
    public void Parent_needs_both_type_and_id()
    {
        Assert.Throws<ArgumentException>(() => Create(objectType: AuditObjectType.Payment, parentType: AuditObjectType.Order, parentId: null));
        Assert.Throws<ArgumentException>(() => Create(objectType: AuditObjectType.Payment, parentType: AuditObjectType.Order, parentId: Guid.Empty));
    }

    [Fact]
    public void Undefined_enum_values_are_rejected() =>
        Assert.Throws<ArgumentException>(() => Create(operation: (AuditOperation)99));

    private const string Marker = "[zanonimizowano]";

    private static readonly HashSet<string> PersonalFields = new(StringComparer.Ordinal) { "customerName", "notes" };

    [Fact]
    public void Redact_replaces_old_and_new_values_of_selected_fields_only()
    {
        var record = Create(changes:
        [
            new("amount", "100.00", "120.50", false),
            new("customerName", "Jan Testowy", "Anna Testowa", true),
            new("notes", "Uwagi przed", "Uwagi po", false),
        ]);

        Assert.True(record.Redact(PersonalFields, Marker));

        Assert.Equal(
            [
                new AuditFieldChange("amount", "100.00", "120.50", false),
                new AuditFieldChange("customerName", Marker, Marker, true),
                new AuditFieldChange("notes", Marker, Marker, false),
            ],
            record.Changes);
    }

    [Fact]
    public void Redact_keeps_null_values_null()
    {
        var created = Create(operation: AuditOperation.Created, changes: [new("customerName", null, "Jan Testowy", true)]);
        var deleted = Create(objectType: AuditObjectType.Refund, parentType: AuditObjectType.Order, parentId: OrderId, operation: AuditOperation.Deleted, changes: [new("reason", "Powód", null, false)]);

        Assert.True(created.Redact(PersonalFields, Marker));
        Assert.True(deleted.Redact(new HashSet<string>(StringComparer.Ordinal) { "reason" }, Marker));

        Assert.Equal(new AuditFieldChange("customerName", null, Marker, true), Assert.Single(created.Changes));
        Assert.Equal(new AuditFieldChange("reason", Marker, null, false), Assert.Single(deleted.Changes));
    }

    [Fact]
    public void Redact_keeps_the_rest_of_the_entry()
    {
        var record = Create(objectType: AuditObjectType.Refund, parentType: AuditObjectType.Order, parentId: OrderId, changes:
        [
            new("amount", "10.00", "20.00", false),
            new("reason", "Stary powód", "Nowy powód", false),
        ]);
        var before = (record.Id, record.OccurredAtUtc, record.AuthorKind, record.AuthorAccountId, record.ObjectType, record.ObjectId,
            record.ObjectBusinessDate, record.ObjectLabel, record.ParentObjectType, record.ParentObjectId, record.Operation);

        record.Redact(new HashSet<string>(StringComparer.Ordinal) { "reason" }, Marker);

        Assert.Equal(before, (record.Id, record.OccurredAtUtc, record.AuthorKind, record.AuthorAccountId, record.ObjectType, record.ObjectId,
            record.ObjectBusinessDate, record.ObjectLabel, record.ParentObjectType, record.ParentObjectId, record.Operation));
        Assert.Equal(["amount", "reason"], record.Changes.Select(change => change.Field));
        Assert.Equal([false, false], record.Changes.Select(change => change.IsPersonalData));
    }

    [Fact]
    public void Redact_without_matching_values_changes_nothing()
    {
        var unrelated = Create(changes: [new("amount", "100.00", "120.50", false)]);
        var alreadyRedacted = Create(changes: [new("customerName", Marker, null, true)]);
        var unrelatedChanges = unrelated.Changes;
        var redactedChanges = alreadyRedacted.Changes;

        Assert.False(unrelated.Redact(PersonalFields, Marker));
        Assert.False(alreadyRedacted.Redact(PersonalFields, Marker));

        Assert.Same(unrelatedChanges, unrelated.Changes);
        Assert.Same(redactedChanges, alreadyRedacted.Changes);
    }

    [Fact]
    public void Redact_requires_fields_and_marker()
    {
        var record = Create();

        Assert.Throws<ArgumentNullException>(() => record.Redact(null!, Marker));
        Assert.Throws<ArgumentException>(() => record.Redact(PersonalFields, " "));
    }

    private static AuditRecord Create(
        AuditAuthorKind authorKind = AuditAuthorKind.Account,
        Guid? accountId = null,
        AuditObjectType objectType = AuditObjectType.Order,
        Guid? objectId = null,
        string label = "ZAM/2026/0042",
        AuditObjectType? parentType = null,
        Guid? parentId = null,
        AuditOperation operation = AuditOperation.Updated,
        AuditFieldChange[]? changes = null,
        bool defaultAccount = true) =>
        AuditRecord.Create(
            Now,
            authorKind,
            authorKind == AuditAuthorKind.Account && defaultAccount && accountId is null ? AccountId : accountId,
            objectType,
            objectId ?? OrderId,
            OrderDate,
            label,
            parentType,
            parentId,
            operation,
            changes ?? AmountChange);
}
