using System.Net;
using System.Text.Json;
using StoneFlow.Api.IntegrationTests.Expenses;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Api.IntegrationTests.Orders;

namespace StoneFlow.Api.IntegrationTests.Reporting;

/// <summary>
/// SF-18: lista „Historia zmian” na prawdziwej bazie — wpisy z zapisów API (wydatek, wpłata) i
/// wpisy wstawione SQL-em (dokładne czasy, remisy), okres w Europe/Warsaw, data biznesowa,
/// rodzaje obiektu, znacznik usunięcia, sortowanie, strony, walidacja, tylko odczyt i dostęp.
/// Zegar: 5.11.2026 12:00 UTC. Dane są fikcyjne.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ChangeHistoryApiTests(SqlServerFixture sqlServer)
{
    private const string Today = "/api/change-history?changedFrom=2026-11-05&changedTo=2026-11-05";

    private const string November = "/api/change-history?changedFrom=2026-11-01&changedTo=2026-11-30";

    [Fact]
    public async Task Expense_added_corrected_and_deleted_gives_three_entries_marked_deleted_with_the_last_values()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        using var create = await test.SendAsync(HttpMethod.Post, "/api/expenses", ExpensesTest.Input(), Guid.NewGuid().ToString());
        var created = await OrderEntryRequests.CreatedAsync(create);
        test.Time.Advance(TimeSpan.FromMinutes(5));
        using var edit = await test.SendAsync(
            HttpMethod.Put, $"/api/expenses/{created.Id}", ExpensesTest.Input(description: "Cięcie blatów", version: created.Version));
        var edited = await OrderEntryRequests.VersionAsync(edit);
        test.Time.Advance(TimeSpan.FromMinutes(5));
        using var delete = await test.SendAsync(HttpMethod.Delete, $"/api/expenses/{created.Id}", new { version = edited });
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var page = await test.GetJsonAsync($"{Today}&direction=asc");
        var items = Items(page);

        Assert.Equal(3, page.GetProperty("total").GetInt32());
        Assert.Equal(["created", "updated", "deleted"], items.Select(i => i.GetProperty("operation").GetString()));
        Assert.Equal(
            ["2026-11-05T12:00:00.0000000Z", "2026-11-05T12:05:00.0000000Z", "2026-11-05T12:10:00.0000000Z"],
            items.Select(i => i.GetProperty("occurredAt").GetString()));
        foreach (var item in items)
        {
            Assert.Equal(JsonValueKind.String, item.GetProperty("id").ValueKind);
            Assert.True(long.TryParse(item.GetProperty("id").GetString(), out _));
            var author = item.GetProperty("author");
            Assert.Equal(["kind"], author.EnumerateObject().Select(p => p.Name));
            Assert.Equal("account", author.GetProperty("kind").GetString());
            var obj = item.GetProperty("object");
            Assert.Equal("expense", obj.GetProperty("type").GetString());
            Assert.Equal(created.Id, obj.GetProperty("id").GetGuid());
            Assert.Equal("2026-10-28", obj.GetProperty("businessDate").GetString());
            Assert.Equal("Wydatek – Cięcie", obj.GetProperty("label").GetString());
            Assert.Equal(JsonValueKind.Null, obj.GetProperty("parent").ValueKind);
            Assert.True(obj.GetProperty("deleted").GetBoolean());
        }

        Assert.DoesNotContain("accountId", page.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal([("description", "Cięcie płyt", "Cięcie blatów", false)], Changes(items[1]));
        var deletion = Changes(items[2]);
        Assert.All(deletion, change => Assert.Null(change.New));
        Assert.Contains(("description", "Cięcie blatów", null, false), deletion);
        Assert.Contains(("grossAmount", "1234.50", null, false), deletion);
        Assert.Contains(("category", "cutting", null, false), deletion);
        Assert.All(Changes(items[0]), change => Assert.Null(change.Old));
    }

    [Fact]
    public async Task Payment_entries_point_to_the_order_and_only_they_are_marked_deleted()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(OrderEntryRequests.Order("1000.00"));
        var payment = await OrderEntryRequests.AddPaymentAsync(test, order.Id, "300.00", "2026-11-04");
        await OrderEntryRequests.DeletePaymentAsync(test, order.Id, payment.Id, payment.Version);

        var payments = Items(await test.GetJsonAsync($"{Today}&objectType=payment"));
        var orders = Items(await test.GetJsonAsync($"{Today}&objectType=order"));

        Assert.Equal(["deleted", "created"], payments.Select(i => i.GetProperty("operation").GetString()));
        Assert.All(payments, item =>
        {
            var obj = item.GetProperty("object");
            Assert.Equal(payment.Id, obj.GetProperty("id").GetGuid());
            Assert.Equal("2026-11-04", obj.GetProperty("businessDate").GetString());
            Assert.Equal("order", obj.GetProperty("parent").GetProperty("type").GetString());
            Assert.Equal(order.Id, obj.GetProperty("parent").GetProperty("id").GetGuid());
            Assert.True(obj.GetProperty("deleted").GetBoolean());
        });
        Assert.NotEmpty(orders);
        Assert.All(orders, item => Assert.False(item.GetProperty("object").GetProperty("deleted").GetBoolean()));
        Assert.All(orders, item => Assert.Equal(JsonValueKind.Null, item.GetProperty("object").GetProperty("parent").ValueKind));
    }

    [Fact]
    public async Task Change_period_is_counted_in_warsaw_time_across_the_end_of_summer_time()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        await InsertAsync(test, "2026-10-24T21:59:59", "before");
        await InsertAsync(test, "2026-10-24T22:00:00", "first");
        await InsertAsync(test, "2026-10-25T22:59:00", "last");
        await InsertAsync(test, "2026-10-25T23:00:00", "after");
        await InsertAsync(test, "2026-11-05T22:30:00", "late-evening");

        var day = await LabelsAsync(test, "/api/change-history?changedFrom=2026-10-25&changedTo=2026-10-25&direction=asc");
        var evening = await LabelsAsync(test, Today);
        var nextDay = await LabelsAsync(test, "/api/change-history?changedFrom=2026-11-06&changedTo=2026-11-06");

        Assert.Equal(["first", "last"], day);
        Assert.Equal(["late-evening"], evening);
        Assert.Empty(nextDay);
    }

    [Fact]
    public async Task Business_date_and_object_type_filters_work_alone_and_together()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = Guid.NewGuid();
        await InsertAsync(test, "2026-11-10T10:00:00", "A-expense-oct", type: "Expense", businessDate: "2026-10-15");
        await InsertAsync(test, "2026-11-10T11:00:00", "B-expense-moved-to-nov", type: "Expense", businessDate: "2026-11-02", operation: "Updated");
        await InsertAsync(test, "2026-11-10T12:00:00", "C-payment-oct", type: "Payment", businessDate: "2026-10-01", parentOrder: order);
        await InsertAsync(test, "2026-11-10T13:00:00", "D-order-oct", type: "Order", id: order, businessDate: "2026-10-31");
        const string Asc = $"{November}&direction=asc";

        Assert.Equal(["A-expense-oct", "B-expense-moved-to-nov", "D-order-oct"], await LabelsAsync(test, $"{Asc}&businessFrom=2026-10-15"));
        Assert.Equal(["A-expense-oct", "C-payment-oct"], await LabelsAsync(test, $"{Asc}&businessTo=2026-10-15"));
        Assert.Equal(
            ["A-expense-oct", "C-payment-oct", "D-order-oct"],
            await LabelsAsync(test, $"{Asc}&businessFrom=2026-10-01&businessTo=2026-10-31"));
        Assert.Equal(["B-expense-moved-to-nov"], await LabelsAsync(test, $"{Asc}&businessFrom=2026-11-01&businessTo=2026-11-30"));
        Assert.Equal(["A-expense-oct", "B-expense-moved-to-nov"], await LabelsAsync(test, $"{Asc}&objectType=expense"));
        Assert.Equal(
            ["A-expense-oct", "B-expense-moved-to-nov", "C-payment-oct"],
            await LabelsAsync(test, $"{Asc}&objectType=expense&objectType=payment"));
        Assert.Equal(
            ["A-expense-oct"],
            await LabelsAsync(test, $"{Asc}&businessFrom=2026-10-01&businessTo=2026-10-31&objectType=expense"));
        Assert.Empty(await LabelsAsync(test, "/api/change-history?changedFrom=2026-10-01&changedTo=2026-10-31&businessFrom=2026-10-01&businessTo=2026-10-31"));
    }

    [Fact]
    public async Task Entries_are_sorted_by_time_then_id_in_the_same_direction_and_paged()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        await InsertAsync(test, "2026-11-04T08:00:00", "E1");
        await InsertAsync(test, "2026-11-04T09:00:00", "E2");
        await InsertAsync(test, "2026-11-04T09:00:00", "E3");
        await InsertAsync(test, "2026-11-04T10:00:00", "E4");

        Assert.Equal(["E4", "E3", "E2", "E1"], await LabelsAsync(test, November));
        Assert.Equal(["E4", "E3", "E2", "E1"], await LabelsAsync(test, November));
        Assert.Equal(["E1", "E2", "E3", "E4"], await LabelsAsync(test, $"{November}&direction=asc"));
        Assert.Equal(["E4", "E3"], await LabelsAsync(test, $"{November}&pageSize=2"));
        Assert.Equal(["E2", "E1"], await LabelsAsync(test, $"{November}&pageSize=2&page=2"));

        var beyond = await test.GetJsonAsync($"{November}&pageSize=2&page=3");
        Assert.Empty(beyond.GetProperty("items").EnumerateArray());
        Assert.Equal(4, beyond.GetProperty("total").GetInt32());
        Assert.Equal(3, beyond.GetProperty("page").GetInt32());
        Assert.Equal(2, beyond.GetProperty("pageSize").GetInt32());
        var defaults = await test.GetJsonAsync(November);
        Assert.Equal(1, defaults.GetProperty("page").GetInt32());
        Assert.Equal(25, defaults.GetProperty("pageSize").GetInt32());
    }

    [Theory]
    [InlineData("changedTo=2026-11-30", "changedFrom")]
    [InlineData("changedFrom=2026-11-01", "changedTo")]
    [InlineData("changedFrom=2026-11-01&changedTo=2026-13-01", "changedTo")]
    [InlineData("changedFrom=2026-11-02&changedTo=2026-11-01", "changedFrom")]
    [InlineData("changedFrom=2026-11-01&changedTo=2026-11-30&businessFrom=2026-10-31&businessTo=2026-10-01", "businessFrom")]
    [InlineData("changedFrom=2026-11-01&changedTo=2026-11-30&businessTo=31.10.2026", "businessTo")]
    [InlineData("changedFrom=2026-11-01&changedTo=2026-11-30&objectType=cost", "objectType")]
    [InlineData("changedFrom=2026-11-01&changedTo=2026-11-30&page=0", "page")]
    [InlineData("changedFrom=2026-11-01&changedTo=2026-11-30&pageSize=101", "pageSize")]
    [InlineData("changedFrom=2026-11-01&changedTo=2026-11-30&direction=up", "direction")]
    public async Task Invalid_parameters_return_400_with_the_key(string query, string key)
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);

        using var response = await test.SendAsync(HttpMethod.Get, $"/api/change-history?{query}");

        Assert.Equal([key], await OrderEntryRequests.ErrorKeysAsync(response));
    }

    [Fact]
    public async Task Deleted_flag_does_not_depend_on_the_filters()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var removed = Guid.NewGuid();
        await InsertAsync(test, "2026-10-10T10:00:00", "removed-created", type: "Expense", id: removed);
        await InsertAsync(test, "2026-11-10T10:00:00", "removed-deleted", type: "Expense", id: removed, operation: "Deleted");
        await InsertAsync(test, "2026-10-11T10:00:00", "kept", type: "Expense");
        const string October = "/api/change-history?changedFrom=2026-10-01&changedTo=2026-10-31";

        var all = Items(await test.GetJsonAsync(October));
        var payments = Items(await test.GetJsonAsync($"{October}&objectType=payment"));
        var expenses = Items(await test.GetJsonAsync($"{October}&objectType=expense"));

        Assert.Equal(
            [("kept", false), ("removed-created", true)],
            all.Select(i => (i.GetProperty("object").GetProperty("label").GetString(), i.GetProperty("object").GetProperty("deleted").GetBoolean())));
        Assert.Empty(payments);
        Assert.Equal(
            [("kept", false), ("removed-created", true)],
            expenses.Select(i => (i.GetProperty("object").GetProperty("label").GetString(), i.GetProperty("object").GetProperty("deleted").GetBoolean())));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Change_history_cannot_be_modified(string method)
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        await InsertAsync(test, "2026-11-04T08:00:00", "E1");

        using var response = await test.SendAsync(new HttpMethod(method), "/api/change-history", new { label = "Zmiana" });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM reporting.AuditEntries"));
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        using var anonymous = AuthClient.Create(test.Factory);

        using var get = await anonymous.Http.GetAsync(new Uri(Today, UriKind.Relative), TestContext.Current.CancellationToken);
        using var post = await anonymous.Http.PostAsync(new Uri("/api/change-history", UriKind.Relative), null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
    }

    [Fact]
    public async Task Business_date_index_exists()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);

        var count = await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.indexes WHERE name = N'IX_AuditEntries_ObjectBusinessDate' "
            + "AND object_id = OBJECT_ID(N'reporting.AuditEntries')");

        Assert.Equal(1, count);
    }

    private static List<JsonElement> Items(JsonElement page) => [.. page.GetProperty("items").EnumerateArray()];

    private static async Task<string?[]> LabelsAsync(OrdersTest test, string path) =>
        [.. Items(await test.GetJsonAsync(path)).Select(i => i.GetProperty("object").GetProperty("label").GetString())];

    private static List<(string? Field, string? Old, string? New, bool Personal)> Changes(JsonElement item) =>
    [
        .. item.GetProperty("changes").EnumerateArray().Select(c => (
            c.GetProperty("field").GetString(),
            c.GetProperty("oldValue").GetString(),
            c.GetProperty("newValue").GetString(),
            c.GetProperty("isPersonalData").GetBoolean())),
    ];

    /// <summary>Wpis audytu wstawiony bezpośrednio (autor systemowy), z dokładnym czasem UTC.</summary>
    private static Task InsertAsync(
        OrdersTest test,
        string occurredAtUtc,
        string label,
        string type = "Expense",
        Guid? id = null,
        string businessDate = "2026-10-28",
        string operation = "Created",
        Guid? parentOrder = null)
    {
        var parentType = parentOrder is null ? "NULL" : "N'Order'";
        var parentId = parentOrder is { } parent ? $"'{parent}'" : "NULL";
        return test.ExecuteSqlAsync(
            "INSERT INTO reporting.AuditEntries (OccurredAtUtc, AuthorKind, AuthorAccountId, ObjectType, ObjectId, ObjectBusinessDate, "
            + "ObjectLabel, ParentObjectType, ParentObjectId, Operation, Changes) "
            + $"VALUES ('{occurredAtUtc}', N'AutomatedJob', NULL, N'{type}', '{id ?? Guid.NewGuid()}', '{businessDate}', "
            + $"N'{label}', {parentType}, {parentId}, N'{operation}', N'[{{\"field\":\"amount\",\"old\":null,\"new\":\"1.00\",\"personal\":false}}]');");
    }
}
