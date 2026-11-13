using StoneFlow.Orders.Application;
using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Application.AnonymizeCustomer;
using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Orders.Tests.Application;

/// <summary>SF-22: kolejność kontroli, transakcja i audyt anonimizacji (fałszywe porty).</summary>
public sealed class AnonymizeCustomerHandlerTests
{
    private static readonly Guid AccountId = Guid.Parse("4c2f8a3e-1b7d-4e55-9a0f-2d6b1c3e5f70");
    private static readonly DateTime OrderCreated = new(2021, 3, 15, 9, 0, 0, DateTimeKind.Utc);

    // 15.03.2026 00:00 w Warszawie (zima, UTC+1).
    private static readonly DateTimeOffset BoundaryWarsawMidnight = new(2026, 3, 14, 23, 0, 0, TimeSpan.Zero);

    private static readonly string VersionText = OrderVersion.Format(AnonymizationFakes.Version);

    private static readonly string[] PersonalValues =
        ["Jan Testowy", "500 600 700", "jan.testowy@example.com", "ul. Przykładowa 1", "00-950", "Warszawa", "1234563218", "Dzwonić po 16", "Pierwszy komentarz", "Zwrot dla Jana", "Rabat dla Jana"];

    private static Order NewOrder() =>
        Order.CreateManual(
            Guid.NewGuid(),
            "ZR-2021-0001",
            TestOrders.Content() with
            {
                OrderDate = new DateOnly(2021, 3, 15),
                Operational = TestOrders.Operational("Dostawca A") with { Notes = "Dzwonić po 16" },
            },
            Guid.NewGuid(),
            new byte[32],
            OrderCreated);

    private static AnonymizationFakes Fakes(DateTimeOffset? now = null, int? months = 60)
    {
        var fakes = new AnonymizationFakes(now ?? BoundaryWarsawMidnight) { Order = NewOrder() };
        fakes.Settings.RetentionMonths = months;
        return fakes;
    }

    private static Task<AnonymizeCustomerResult> Run(AnonymizationFakes fakes, string? version = null, Guid? orderId = null) =>
        fakes.Handler().HandleAsync(new AnonymizeCustomerCommand(AccountId, orderId ?? fakes.Order!.Id, version ?? VersionText), CancellationToken.None);

    [Theory]
    [InlineData("")]
    [InlineData("nie-wersja")]
    public async Task Invalid_version_touches_nothing(string version)
    {
        var fakes = Fakes();

        var result = await Run(fakes, version);

        Assert.Equal(AnonymizeCustomerOutcome.ValidationFailed, result.Outcome);
        Assert.NotNull(result.Errors);
        Assert.Empty(fakes.Calls);
    }

    [Fact]
    public async Task Disabled_touches_nothing_even_for_missing_order()
    {
        var fakes = Fakes(months: null);

        var existing = await Run(fakes);
        var missing = await Run(fakes, orderId: Guid.NewGuid());

        Assert.Equal(AnonymizeCustomerOutcome.Disabled, existing.Outcome);
        Assert.Equal(AnonymizeCustomerOutcome.Disabled, missing.Outcome);
        Assert.Empty(fakes.Calls);
    }

    [Fact]
    public async Task Missing_order_is_not_found()
    {
        var fakes = Fakes();

        var result = await Run(fakes, orderId: Guid.NewGuid());

        Assert.Equal(AnonymizeCustomerOutcome.NotFound, result.Outcome);
        Assert.Equal(["begin", "lock"], fakes.Calls);
    }

    [Fact]
    public async Task Version_is_checked_before_state()
    {
        var fakes = Fakes();
        fakes.Order!.AnonymizeCustomer(AccountId, OrderCreated);

        var stale = await Run(fakes, OrderVersion.Format([9, 9, 9, 9, 9, 9, 9, 9]));
        var current = await Run(fakes);

        Assert.Equal(AnonymizeCustomerOutcome.Conflict, stale.Outcome);
        Assert.Equal(AnonymizeCustomerOutcome.AlreadyAnonymized, current.Outcome);
        Assert.DoesNotContain("save", fakes.Calls);
    }

    [Fact]
    public async Task Calls_happen_in_order_and_commit_only_saved()
    {
        var fakes = Fakes();

        var result = await Run(fakes);

        Assert.Equal(AnonymizeCustomerOutcome.Saved, result.Outcome);
        Assert.Equal(AnonymizationFakes.NewVersion, result.Version);
        Assert.Equal(["begin", "lock", "load", "facts", "activity", "entries", "save", "redact", "audit"], fakes.Calls);
        Assert.True(fakes.UnitOfWork.Committed);
    }

    [Fact]
    public async Task Day_before_boundary_is_rejected_and_boundary_day_is_saved()
    {
        var before = Fakes(BoundaryWarsawMidnight.AddSeconds(-1));
        var onDay = Fakes(BoundaryWarsawMidnight);

        var rejected = await Run(before);
        var saved = await Run(onDay);

        Assert.Equal(AnonymizeCustomerOutcome.RetentionNotElapsed, rejected.Outcome);
        Assert.Equal(new DateOnly(2026, 3, 15), rejected.AvailableFrom);
        Assert.DoesNotContain("save", before.Calls);
        Assert.False(before.UnitOfWork.Committed);
        Assert.Equal(AnonymizeCustomerOutcome.Saved, saved.Outcome);
    }

    [Fact]
    public async Task Save_conflict_rolls_back_without_audit()
    {
        var fakes = Fakes();
        fakes.Store.SaveStatus = StoreSaveStatus.Conflict;

        var result = await Run(fakes);

        Assert.Equal(AnonymizeCustomerOutcome.Conflict, result.Outcome);
        Assert.DoesNotContain("redact", fakes.Calls);
        Assert.Empty(fakes.AuditLog.Entries);
        Assert.False(fakes.UnitOfWork.Committed);
    }

    [Fact]
    public async Task Deleted_entry_recorded_in_audit_today_keeps_it_pending()
    {
        var fakes = Fakes(new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero));
        fakes.Activity.LastRecordedAtUtc = new DateTime(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);

        var result = await Run(fakes);

        Assert.Equal(AnonymizeCustomerOutcome.RetentionNotElapsed, result.Outcome);
        Assert.Equal(new DateOnly(2031, 11, 13), result.AvailableFrom);
    }

    [Fact]
    public async Task Audit_holds_no_personal_values_and_no_updated_entry()
    {
        var fakes = Fakes();
        fakes.EntryStore.Refunds.Add(NewRefund(fakes.Order!.Id, "Zwrot dla Jana"));
        fakes.EntryStore.Corrections.Add(NewCorrection(fakes.Order.Id, "Rabat dla Jana"));

        await Run(fakes);

        Assert.DoesNotContain(fakes.AuditLog.Entries, entry => entry.Operation != AuditOperation.Anonymized);
        var values = fakes.AuditLog.Entries
            .SelectMany(entry => entry.Changes.SelectMany(change => new[] { change.OldValue, change.NewValue }).Append(entry.Object.Label))
            .ToList();
        Assert.DoesNotContain(values, value => value is not null && PersonalValues.Any(personal => value.Contains(personal, StringComparison.Ordinal)));
        Assert.All(fakes.AuditLog.Entries.SelectMany(entry => entry.Changes), change =>
        {
            Assert.Null(change.OldValue);
            Assert.False(change.IsPersonalData);
        });
    }

    [Fact]
    public async Task Records_scope_counts_only_changed_reasons_and_redacts_the_order()
    {
        var fakes = Fakes();
        var orderId = fakes.Order!.Id;
        fakes.EntryStore.Refunds.Add(NewRefund(orderId, "Zwrot dla Jana"));
        fakes.EntryStore.Refunds.Add(NewRefund(orderId, CustomerAnonymization.Marker));
        fakes.EntryStore.Corrections.Add(NewCorrection(orderId, "Rabat dla Jana"));

        await Run(fakes);

        var orderEntry = fakes.AuditLog.Entries[0];
        Assert.Equal(AuditObjectType.Order, orderEntry.Object.Type);
        Assert.Equal(
            [
                ("anonymizedFields", """["customerName","customerPhone","customerEmail","customerStreet","customerPostalCode","customerCity","customerTaxId","notes","comment"]"""),
                ("anonymizedReasons", "2"),
                ("retentionStartDate", "2021-03-15"),
                ("retentionMonths", "60"),
            ],
            orderEntry.Changes.Select(change => (change.Field, change.NewValue!)));
        Assert.Equal(3, fakes.AuditLog.Entries.Count);
        Assert.Equal(
            [AuditObjectType.Refund, AuditObjectType.SalesCorrection],
            fakes.AuditLog.Entries.Skip(1).Select(entry => entry.Object.Type));

        var redaction = Assert.Single(fakes.Redaction.Redactions);
        Assert.Equal(new AuditObjectReference(AuditObjectType.Order, orderId), redaction.Root);
        Assert.Equal(CustomerAnonymization.Marker, redaction.Marker);
        Assert.Equal(OrderAnonymizationAudit.Fields, redaction.Fields);
    }

    [Theory]
    [InlineData(AnonymizeCustomerOutcome.Saved, true)]
    [InlineData(AnonymizeCustomerOutcome.NotFound, false)]
    [InlineData(AnonymizeCustomerOutcome.Conflict, false)]
    [InlineData(AnonymizeCustomerOutcome.AlreadyAnonymized, false)]
    [InlineData(AnonymizeCustomerOutcome.RetentionNotElapsed, false)]
    public async Task Commits_only_saved(AnonymizeCustomerOutcome outcome, bool commit)
    {
        var fakes = Fakes();
        await Run(fakes);

        Assert.Equal(commit, fakes.UnitOfWork.CommitWhen!(new AnonymizeCustomerResult(outcome)));
    }

    private static Refund NewRefund(Guid orderId, string reason) =>
        Refund.Create(
            Guid.NewGuid(),
            orderId,
            new RefundDetails(RefundKind.MoneyReturn, 10m, new DateOnly(2021, 3, 15), RefundMethod.Transfer, reason),
            Guid.NewGuid(),
            new byte[32],
            OrderCreated);

    private static SalesCorrection NewCorrection(Guid orderId, string reason) =>
        SalesCorrection.Create(
            Guid.NewGuid(),
            orderId,
            new SalesCorrectionDetails(10m, new DateOnly(2021, 3, 15), reason),
            Guid.NewGuid(),
            new byte[32],
            OrderCreated);
}
