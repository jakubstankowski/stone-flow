using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Api.IntegrationTests.Orders;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Application.Persistence;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Api.IntegrationTests.Audit;

/// <summary>
/// SF-22: anonimizacja zapisanych wpisów audytu (<see cref="IAuditRedaction"/>) i odczyt ostatniej
/// aktywności (<see cref="IAuditActivity"/>) na prawdziwej bazie — zakres korzenia i obiektów podrzędnych,
/// inne obiekty nietknięte, wymóg transakcji. Dane fikcyjne.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class AuditRedactionTests(SqlServerFixture sqlServer)
{
    private const string Marker = "[zanonimizowano]";

    private static readonly Guid AccountId = Guid.Parse("4c2f8a3e-1b7d-4e55-9a0f-2d6b1c3e5f70");
    private static readonly DateOnly Date = new(2026, 10, 28);

    private static readonly AuditFieldSelector[] Selectors =
    [
        new(AuditObjectType.Order, "customerName"),
        new(AuditObjectType.Refund, "reason"),
    ];

    [Fact]
    public async Task Redacts_root_and_children_including_deletions_and_leaves_other_objects()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var orderId = Guid.NewGuid();
        var otherOrderId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();
        var refundId = Guid.NewGuid();
        var order = new AuditObjectReference(AuditObjectType.Order, orderId);
        await RecordAsync(
            test,
            new AuditEntry(Author, new AuditObject(AuditObjectType.Order, orderId, Date, "ZR-2026-0001"), AuditOperation.Created,
                [new("customerName", null, "Jan Testowy", true), new("value", null, "100.00")]),
            new AuditEntry(Author, new AuditObject(AuditObjectType.Order, orderId, Date, "ZR-2026-0001"), AuditOperation.Updated,
                [new("customerName", "Jan Testowy", "Jan Testowy-Nowak", true)]),
            new AuditEntry(Author, new AuditObject(AuditObjectType.Refund, refundId, Date, "Zwrot do ZR-2026-0001", order), AuditOperation.Deleted,
                [new("amount", "10.00", null), new("reason", "Zwrot dla Jana", null)]),
            new AuditEntry(Author, new AuditObject(AuditObjectType.Payment, Guid.NewGuid(), Date, "Wpłata do ZR-2026-0001", order), AuditOperation.Created,
                [new("amount", null, "100.00"), new("receivedBy", null, "Anna Przyjmująca", true)]),
            new AuditEntry(Author, new AuditObject(AuditObjectType.Order, otherOrderId, Date, "ZR-2026-0002"), AuditOperation.Created,
                [new("customerName", null, "Jan Testowy", true)]),
            new AuditEntry(Author, new AuditObject(AuditObjectType.Expense, expenseId, Date, "Wydatek – test"), AuditOperation.Created,
                [new("counterparty", null, "Jan Testowy", true)]));
        var otherBefore = await ChangesOfAsync(test, otherOrderId, expenseId);

        var changed = await RedactAsync(test, new AuditRedaction(order, Selectors, Marker));

        Assert.Equal(3, changed);
        var rows = await AnonymizationTestSupport.AuditAsync(test, orderId);
        Assert.Equal(4, rows.Count);
        Assert.Equal([("customerName", (string?)null, (string?)Marker, true), ("value", null, "100.00", false)], AnonymizationTestSupport.Changes(rows[0].Changes));
        Assert.Equal([("customerName", (string?)Marker, (string?)Marker, true)], AnonymizationTestSupport.Changes(rows[1].Changes));
        Assert.Equal([("amount", (string?)"10.00", (string?)null, false), ("reason", Marker, null, false)], AnonymizationTestSupport.Changes(rows[2].Changes));
        Assert.Equal([("amount", (string?)null, (string?)"100.00", false), ("receivedBy", null, "Anna Przyjmująca", true)], AnonymizationTestSupport.Changes(rows[3].Changes));
        Assert.Equal(["ZR-2026-0001", "ZR-2026-0001", "Zwrot do ZR-2026-0001", "Wpłata do ZR-2026-0001"], rows.Select(row => row.Label));
        Assert.Equal(["Created", "Updated", "Deleted", "Created"], rows.Select(row => row.Operation));
        Assert.Equal(otherBefore, await ChangesOfAsync(test, otherOrderId, expenseId));
    }

    [Fact]
    public async Task Object_without_entries_gives_zero_and_no_activity()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var root = new AuditObjectReference(AuditObjectType.Order, Guid.NewGuid());

        Assert.Equal(0, await RedactAsync(test, new AuditRedaction(root, Selectors, Marker)));
        Assert.Null(await LastActivityAsync(test, root));
    }

    [Fact]
    public async Task Redaction_outside_unit_of_work_is_rejected_and_activity_works_outside()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var orderId = Guid.NewGuid();
        var order = new AuditObjectReference(AuditObjectType.Order, orderId);
        await RecordAsync(
            test,
            new AuditEntry(Author, new AuditObject(AuditObjectType.Order, orderId, Date, "ZR-2026-0001"), AuditOperation.Created,
                [new("customerName", null, "Jan Testowy", true)]));
        test.Time.Advance(TimeSpan.FromHours(2));
        await RecordAsync(
            test,
            new AuditEntry(Author, new AuditObject(AuditObjectType.Refund, Guid.NewGuid(), Date, "Zwrot do ZR-2026-0001", order), AuditOperation.Deleted,
                [new("amount", "10.00", null)]));

        using var scope = test.Factory.Services.CreateScope();
        var redaction = scope.ServiceProvider.GetRequiredService<IAuditRedaction>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            redaction.RedactAsync(new AuditRedaction(order, Selectors, Marker), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            redaction.RedactAsync(new AuditRedaction(order, [], Marker), TestContext.Current.CancellationToken));
        var last = await LastActivityAsync(test, order);
        Assert.Equal(OrdersTest.DefaultNow.UtcDateTime.AddHours(2), last);
        Assert.Equal(DateTimeKind.Utc, last!.Value.Kind);
    }

    private static AuditAuthor Author => AuditAuthor.Account(AccountId);

    private static async Task RecordAsync(OrdersTest test, params AuditEntry[] entries)
    {
        using var scope = test.Factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
            async ct =>
            {
                foreach (var entry in entries)
                {
                    await services.GetRequiredService<IAuditLog>().RecordAsync(entry, ct);
                }

                return true;
            },
            commitWhen: _ => true,
            TestContext.Current.CancellationToken);
    }

    private static async Task<int> RedactAsync(OrdersTest test, AuditRedaction redaction)
    {
        using var scope = test.Factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        return await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
            ct => services.GetRequiredService<IAuditRedaction>().RedactAsync(redaction, ct),
            commitWhen: _ => true,
            TestContext.Current.CancellationToken);
    }

    private static async Task<DateTime?> LastActivityAsync(OrdersTest test, AuditObjectReference root)
    {
        using var scope = test.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAuditActivity>().LastRecordedAtUtcAsync(root, TestContext.Current.CancellationToken);
    }

    private static Task<IReadOnlyList<string>> ChangesOfAsync(OrdersTest test, params Guid[] ids) =>
        test.RowsAsync(
            $"SELECT Changes FROM reporting.AuditEntries WHERE ObjectId IN ({string.Join(", ", ids.Select(id => $"'{id}'"))}) ORDER BY Id",
            reader => reader.GetString(0));
}
