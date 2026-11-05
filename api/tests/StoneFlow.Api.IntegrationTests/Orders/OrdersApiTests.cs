using System.Data.SqlTypes;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using StoneFlow.Access.Api;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Orders.Api;

namespace StoneFlow.Api.IntegrationTests.Orders;

/// <summary>
/// SF-8: zamówienia przez API na prawdziwej bazie — idempotencja, numeracja w transakcji,
/// kontrola wersji, reguły statusu i pól sklepu, audyt, lista oraz ochrona dostępu.
/// Dane klientów są fikcyjne.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class OrdersApiTests(SqlServerFixture sqlServer)
{
    private static readonly int Year = TimeZoneInfo.ConvertTime(
        DateTimeOffset.UtcNow,
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Warsaw", out var zone) ? zone : TimeZoneInfo.Utc).Year;

    [Fact]
    public async Task Create_returns_201_with_number_and_the_order_appears_on_the_list()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);

        using var response = await test.CreateAsync(Input(value: "0"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await Json(response);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal($"ZR-{Year}-0001", body.GetProperty("number").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("version").GetString()));
        Assert.Equal($"/api/orders/{id}", response.Headers.Location?.OriginalString);

        var list = await test.GetJsonAsync("/api/orders");
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal(id, item.GetProperty("id").GetGuid());
        Assert.Equal("manual", item.GetProperty("source").GetString());
        Assert.Equal("olx", item.GetProperty("channel").GetString());
        Assert.Equal("0.00", item.GetProperty("value").GetString());
        Assert.Equal("inProgress", item.GetProperty("fulfillmentStatus").GetString());
        Assert.Equal("2026-10-28", item.GetProperty("orderDate").GetString());

        var details = await test.GetJsonAsync($"/api/orders/{id}");
        Assert.Equal("Jan Testowy", details.GetProperty("customer").GetProperty("name").GetString());
        Assert.Equal("1234563218", details.GetProperty("customer").GetProperty("taxId").GetString());
        Assert.Equal(0, details.GetProperty("shopOwnedFields").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, details.GetProperty("externalId").ValueKind);
        Assert.EndsWith("Z", details.GetProperty("createdAtUtc").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manual_order_with_shop_channel_keeps_manual_source()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);

        var created = await test.CreateOrderAsync(Input(channel: "shop"));
        var details = await test.GetJsonAsync($"/api/orders/{created.Id}");

        Assert.Equal("shop", details.GetProperty("channel").GetString());
        Assert.Equal("manual", details.GetProperty("source").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nie-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Create_without_a_valid_idempotency_key_returns_400(string? key)
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);

        using var response = await test.SendAsync(HttpMethod.Post, "/api/orders", Input(), idempotencyKey: key);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OrderProblems.IdempotencyKeyRequiredCode, (await Json(response)).GetProperty("code").GetString());
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Orders"));
    }

    [Fact]
    public async Task Repeated_create_with_the_same_key_returns_200_with_the_same_order()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var key = Guid.NewGuid();

        using var first = await test.CreateAsync(Input(), key);
        // Te same dane po normalizacji: spacje, zapis NIP i jawny status domyślny.
        using var second = await test.CreateAsync(Input(name: "  Jan Testowy ", taxId: "123-456-32-18", status: "inProgress"), key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var created = await Json(first);
        var repeated = await Json(second);
        Assert.Equal(created.GetProperty("id").GetGuid(), repeated.GetProperty("id").GetGuid());
        Assert.Equal(created.GetProperty("number").GetString(), repeated.GetProperty("number").GetString());
        Assert.Equal(created.GetProperty("version").GetString(), repeated.GetProperty("version").GetString());
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Orders"));
    }

    [Fact]
    public async Task Same_key_with_different_data_returns_409_with_the_existing_order()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var key = Guid.NewGuid();
        var created = await test.CreateOrderAsync(Input(), key);

        using var response = await test.CreateAsync(Input(value: "99.00"), key);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await Json(response);
        Assert.Equal(OrderProblems.IdempotencyKeyReusedCode, problem.GetProperty("code").GetString());
        Assert.Equal(created.Id, problem.GetProperty("orderId").GetGuid());
        Assert.Equal(created.Number, problem.GetProperty("orderNumber").GetString());
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Orders"));
    }

    [Fact]
    public async Task Invalid_create_returns_400_with_json_path_keys()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);

        using var invalid = await test.CreateAsync(Input(value: "-1", name: " ", taxId: "1234567890", channel: "allegro", postalCode: "00950"));
        using var cancelled = await test.CreateAsync(Input(status: "cancelled"));
        using var scale = await test.CreateAsync(Input(value: "1.234", suppliers: ["Dostawca A", new string('d', 101)]));

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = (await Json(invalid)).GetProperty("errors");
        Assert.Equal(
            ["channel", "customer.name", "customer.postalCode", "customer.taxId", "value"],
            errors.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.BadRequest, cancelled.StatusCode);
        Assert.True((await Json(cancelled)).GetProperty("errors").TryGetProperty("fulfillmentStatus", out _));
        var scaleErrors = (await Json(scale)).GetProperty("errors");
        Assert.True(scaleErrors.TryGetProperty("value", out _));
        Assert.True(scaleErrors.TryGetProperty("operational.suppliers[1]", out _));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Orders"));
    }

    [Fact]
    public async Task Parallel_creates_with_same_key_create_one_order()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var key = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => test.CreateAsync(Input(), key)));
        try
        {
            Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, r.StatusCode.ToString()));
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            var ids = new HashSet<Guid>();
            foreach (var response in responses)
            {
                ids.Add((await Json(response)).GetProperty("id").GetGuid());
            }

            Assert.Single(ids);
            Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Orders"));
            Assert.Equal(1, await test.ScalarAsync<int>($"SELECT LastNumber FROM orders.OrderNumberCounters WHERE Year = {Year}"));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Parallel_creates_get_distinct_consecutive_numbers()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => test.CreateAsync(Input())));
        try
        {
            var numbers = new List<string>();
            foreach (var response in responses)
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                numbers.Add((await Json(response)).GetProperty("number").GetString()!);
            }

            Assert.Equal(
                Enumerable.Range(1, 5).Select(n => $"ZR-{Year}-{n:D4}"),
                numbers.Order(StringComparer.Ordinal));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Audit_failure_rolls_back_order_and_number()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        await test.ExecuteSqlAsync("ALTER TABLE reporting.AuditEntries ADD CONSTRAINT CK_Test_RejectAll CHECK (ObjectLabel = N'nigdy');");

        using var failed = await test.CreateAsync(Input());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Orders"));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.OrderNumberCounters"));

        await test.ExecuteSqlAsync("ALTER TABLE reporting.AuditEntries DROP CONSTRAINT CK_Test_RejectAll;");
        var created = await test.CreateOrderAsync(Input());

        Assert.Equal($"ZR-{Year}-0001", created.Number);
    }

    [Fact]
    public async Task Create_edit_cancel_restore_write_audit_entries()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var created = await test.CreateOrderAsync(Input());

        var edited = await test.EditAsync(created.Id, Input(value: "1300.00", orderDate: "2026-11-02", version: created.Version));
        var cancelled = await test.StatusActionAsync(created.Id, "cancel", edited);
        var restored = await test.StatusActionAsync(created.Id, "restore", cancelled);

        var rows = await test.AuditRowsAsync(created.Id);
        Assert.Equal(["Created", "Updated", "Updated", "Updated"], rows.Select(r => r.Operation));
        Assert.All(rows, r => Assert.Equal("Account", r.AuthorKind));
        Assert.All(rows, r => Assert.Equal(created.Number, r.Label));
        Assert.Equal(new DateTime(2026, 10, 28), rows[0].BusinessDate);
        Assert.Equal(new DateTime(2026, 11, 2), rows[1].BusinessDate);

        var createdChanges = Changes(rows[0]);
        Assert.Contains(createdChanges, c => c.Field == "customerName" && c.Personal && c.New == "Jan Testowy");
        Assert.Contains(createdChanges, c => c.Field == "customerKind" && !c.Personal && c.New == "person");
        Assert.Contains(createdChanges, c => c.Field == "value" && c.Old is null && c.New == "1234.50");
        Assert.Equal(
            [("orderDate", "2026-10-28", "2026-11-02"), ("value", "1234.50", "1300.00")],
            Changes(rows[1]).Select(c => (c.Field, c.Old, c.New)));
        Assert.Equal([("fulfillmentStatus", "inProgress", "cancelled")], Changes(rows[2]).Select(c => (c.Field, c.Old, c.New)));
        Assert.Equal([("fulfillmentStatus", "cancelled", "inProgress")], Changes(rows[3]).Select(c => (c.Field, c.Old, c.New)));

        var details = await test.GetJsonAsync($"/api/orders/{created.Id}");
        Assert.Equal("inProgress", details.GetProperty("fulfillmentStatus").GetString());
        Assert.Equal(restored, details.GetProperty("version").GetString());
    }

    [Fact]
    public async Task Edit_without_changes_writes_no_audit_and_keeps_version()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var created = await test.CreateOrderAsync(Input());
        var before = await test.GetJsonAsync($"/api/orders/{created.Id}");

        var version = await test.EditAsync(created.Id, Input(name: " Jan Testowy ", status: "inProgress", version: created.Version));

        Assert.Equal(created.Version, version);
        Assert.Single(await test.AuditRowsAsync(created.Id));
        var after = await test.GetJsonAsync($"/api/orders/{created.Id}");
        Assert.Equal(before.GetProperty("updatedAtUtc").GetString(), after.GetProperty("updatedAtUtc").GetString());
    }

    [Fact]
    public async Task Edit_with_stale_version_returns_409_and_changes_nothing_even_for_other_fields()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var created = await test.CreateOrderAsync(Input());

        // Dwie karty wczytały tę samą wersję; pierwsza zmienia wartość, druga tylko komentarz.
        await test.EditAsync(created.Id, Input(value: "2000.00", version: created.Version));
        using var stale = await test.SendAsync(HttpMethod.Put, $"/api/orders/{created.Id}", Input(comment: "Inny komentarz", version: created.Version));
        using var staleCancelledStatus = await test.SendAsync(
            HttpMethod.Put, $"/api/orders/{created.Id}", Input(status: "cancelled", version: created.Version));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(OrderProblems.ConflictCode, (await Json(stale)).GetProperty("code").GetString());
        Assert.Equal(OrderProblems.ConflictCode, (await Json(staleCancelledStatus)).GetProperty("code").GetString());
        var details = await test.GetJsonAsync($"/api/orders/{created.Id}");
        Assert.Equal("2000.00", details.GetProperty("value").GetString());
        Assert.Equal("Komentarz", details.GetProperty("comment").GetString());
        Assert.Equal(2, (await test.AuditRowsAsync(created.Id)).Count);
    }

    [Fact]
    public async Task Edit_rules_for_cancelled_status()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var created = await test.CreateOrderAsync(Input());

        using var cancelOnActive = await test.SendAsync(HttpMethod.Put, $"/api/orders/{created.Id}", Input(status: "cancelled", version: created.Version));
        var cancelledVersion = await test.StatusActionAsync(created.Id, "cancel", created.Version);
        using var reopen = await test.SendAsync(HttpMethod.Put, $"/api/orders/{created.Id}", Input(status: "completed", version: cancelledVersion));
        var edited = await test.EditAsync(created.Id, Input(status: "cancelled", comment: "Poprawka po anulowaniu", version: cancelledVersion));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, cancelOnActive.StatusCode);
        Assert.Equal(OrderProblems.StatusChangeRequiresActionCode, (await Json(cancelOnActive)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reopen.StatusCode);
        Assert.Equal(OrderProblems.StatusChangeRequiresActionCode, (await Json(reopen)).GetProperty("code").GetString());
        Assert.NotEqual(cancelledVersion, edited);
        var details = await test.GetJsonAsync($"/api/orders/{created.Id}");
        Assert.Equal("cancelled", details.GetProperty("fulfillmentStatus").GetString());
        Assert.Equal("Poprawka po anulowaniu", details.GetProperty("comment").GetString());
        Assert.Equal(("comment", "Komentarz", "Poprawka po anulowaniu"), Changes((await test.AuditRowsAsync(created.Id))[^1]).Select(c => (c.Field, c.Old, c.New)).Single());
    }

    [Fact]
    public async Task Edit_validates_manual_order_fields_after_loading()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var created = await test.CreateOrderAsync(Input());

        using var missingVersion = await test.SendAsync(HttpMethod.Put, $"/api/orders/{created.Id}", Input());
        using var invalidValue = await test.SendAsync(HttpMethod.Put, $"/api/orders/{created.Id}", Input(value: "-5", version: created.Version));
        using var unknown = await test.SendAsync(HttpMethod.Put, $"/api/orders/{Guid.NewGuid()}", Input(value: "-5", version: created.Version));

        Assert.Equal(HttpStatusCode.BadRequest, missingVersion.StatusCode);
        Assert.True((await Json(missingVersion)).GetProperty("errors").TryGetProperty("version", out _));
        Assert.Equal(HttpStatusCode.BadRequest, invalidValue.StatusCode);
        Assert.True((await Json(invalidValue)).GetProperty("errors").TryGetProperty("value", out _));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(OrderProblems.NotFoundCode, (await Json(unknown)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Shop_order_rejects_changes_of_shop_owned_fields()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var id = await test.SeedShopOrderAsync();
        var details = await test.GetJsonAsync($"/api/orders/{id}");
        var version = details.GetProperty("version").GetString()!;

        Assert.Equal("wooCommerce", details.GetProperty("source").GetString());
        Assert.Equal("1001", details.GetProperty("externalId").GetString());
        Assert.Equal("processing", details.GetProperty("shopStatus").GetString());
        Assert.Equal(["orderDate", "value", "channel", "customer"], details.GetProperty("shopOwnedFields").EnumerateArray().Select(f => f.GetString()));

        foreach (var (input, field) in new (object Input, string Field)[]
        {
            (ShopInput(version, value: "1.00"), "value"),
            (ShopInput(version, orderDate: "2026-01-01"), "orderDate"),
            (ShopInput(version, channel: "phone"), "channel"),
            (ShopInput(version, name: "Inny Klient Testowy"), "customer"),
        })
        {
            using var response = await test.SendAsync(HttpMethod.Put, $"/api/orders/{id}", input);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            var problem = await Json(response);
            Assert.Equal(OrderProblems.FieldOwnedByShopCode, problem.GetProperty("code").GetString());
            Assert.Equal([field], problem.GetProperty("fields").EnumerateArray().Select(f => f.GetString()));
        }

        var equal = await test.EditAsync(id, ShopInput(version, comment: "Uwaga ERP"));
        var withoutShopFields = await test.EditAsync(
            id,
            new { fulfillmentStatus = "completed", operational = new { suppliers = new[] { "Dostawca A" } }, comment = "Uwaga ERP", version = equal });

        var after = await test.GetJsonAsync($"/api/orders/{id}");
        Assert.Equal(withoutShopFields, after.GetProperty("version").GetString());
        Assert.Equal("250.00", after.GetProperty("value").GetString());
        Assert.Equal("shop", after.GetProperty("channel").GetString());
        Assert.Equal("completed", after.GetProperty("fulfillmentStatus").GetString());
        Assert.Equal("Uwaga ERP", after.GetProperty("comment").GetString());
    }

    [Fact]
    public async Task Cancel_and_restore_check_existence_version_and_state()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var created = await test.CreateOrderAsync(Input());

        using var restoreActive = await test.SendAsync(HttpMethod.Post, $"/api/orders/{created.Id}/restore", new { version = created.Version });
        var cancelled = await test.StatusActionAsync(created.Id, "cancel", created.Version);
        using var staleCancel = await test.SendAsync(HttpMethod.Post, $"/api/orders/{created.Id}/cancel", new { version = created.Version });
        using var cancelAgain = await test.SendAsync(HttpMethod.Post, $"/api/orders/{created.Id}/cancel", new { version = cancelled });
        using var unknown = await test.SendAsync(HttpMethod.Post, $"/api/orders/{Guid.NewGuid()}/cancel", new { version = cancelled });
        using var withoutVersion = await test.SendAsync(HttpMethod.Post, $"/api/orders/{created.Id}/restore", new { });

        Assert.Equal(HttpStatusCode.Conflict, restoreActive.StatusCode);
        Assert.Equal(OrderProblems.NotCancelledCode, (await Json(restoreActive)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, staleCancel.StatusCode);
        Assert.Equal(OrderProblems.ConflictCode, (await Json(staleCancel)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, cancelAgain.StatusCode);
        Assert.Equal(OrderProblems.AlreadyCancelledCode, (await Json(cancelAgain)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withoutVersion.StatusCode);
        Assert.Equal(2, (await test.AuditRowsAsync(created.Id)).Count);
    }

    [Fact]
    public async Task Get_unknown_order_returns_404()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);

        using var response = await test.SendAsync(HttpMethod.Get, $"/api/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(OrderProblems.NotFoundCode, (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Delete_is_not_possible()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var created = await test.CreateOrderAsync(Input());

        using var response = await test.SendAsync(HttpMethod.Delete, $"/api/orders/{created.Id}");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Orders"));
    }

    [Fact]
    public async Task List_filters_sorts_and_pages_in_the_database()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var a = await test.CreateOrderAsync(Input(orderDate: "2026-10-01", channel: "olx", value: "300.00", name: "Anna Kot Testowa"));
        var b = await test.CreateOrderAsync(Input(orderDate: "2026-10-15", channel: "phone", value: "100.00", name: "Rabat 50% Testowy", status: "problem"));
        var c = await test.CreateOrderAsync(Input(orderDate: "2026-10-31", channel: "phone", value: "200.00", name: "Zenon 500 Testowy", status: "completed"));
        var d = await test.CreateOrderAsync(Input(orderDate: "2026-10-31", channel: "other", value: "50.00", name: "Klient_Testowy"));
        var shop = await test.SeedShopOrderAsync();

        // Domyślnie data malejąco; remis (c i d z tą samą datą) rozstrzyga Id w porządku SQL Server, stabilnie.
        var manual = await test.IdsAsync("/api/orders?source=manual");
        Assert.Equal([.. new[] { c.Id, d.Id }.OrderByDescending(id => new SqlGuid(id)), b.Id, a.Id], manual);
        Assert.Equal(manual, await test.IdsAsync("/api/orders?source=manual"));
        Assert.Equal([shop], await test.IdsAsync("/api/orders?source=wooCommerce"));
        Assert.Equal([b.Id, a.Id], await test.IdsAsync("/api/orders?dateFrom=2026-10-01&dateTo=2026-10-15"));
        Assert.Equal([c.Id, b.Id], (await test.IdsAsync("/api/orders?channel=phone")).ToArray());
        Assert.Equal(
            new HashSet<Guid> { b.Id, c.Id },
            (await test.IdsAsync("/api/orders?status=problem&status=completed")).ToHashSet());
        Assert.Equal([b.Id], await test.IdsAsync("/api/orders?search=50%25"));
        Assert.Equal([d.Id], await test.IdsAsync("/api/orders?search=t_T"));
        Assert.Equal([a.Id], await test.IdsAsync("/api/orders?search=anna%20kot"));
        Assert.Equal([a.Id], await test.IdsAsync($"/api/orders?search={a.Number[^4..]}&source=manual"));
        Assert.Equal([d.Id, b.Id, c.Id, a.Id], await test.IdsAsync("/api/orders?source=manual&sort=value&direction=asc"));
        Assert.Equal([a.Id, d.Id, b.Id, c.Id], await test.IdsAsync("/api/orders?source=manual&sort=customer&direction=asc"));
        Assert.Equal([d.Id, c.Id, b.Id, a.Id], await test.IdsAsync("/api/orders?source=manual&sort=number&direction=desc"));

        var page = await test.GetJsonAsync("/api/orders?pageSize=2&page=2&source=manual");
        Assert.Equal(4, page.GetProperty("total").GetInt32());
        Assert.Equal(2, page.GetProperty("page").GetInt32());
        Assert.Equal(2, page.GetProperty("pageSize").GetInt32());
        Assert.Equal([b.Id, a.Id], page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));

        var beyond = await test.GetJsonAsync("/api/orders?page=99");
        Assert.Equal(0, beyond.GetProperty("items").GetArrayLength());
        Assert.Equal(5, beyond.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("page=0", "page")]
    [InlineData("dateFrom=2026-10-31&dateTo=2026-10-01", "dateTo")]
    [InlineData("sort=amount", "sort")]
    [InlineData("status=lost", "status")]
    [InlineData("channel=allegro", "channel")]
    public async Task Invalid_list_parameters_return_400(string query, string key)
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);

        using var response = await test.SendAsync(HttpMethod.Get, $"/api/orders?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await Json(response)).GetProperty("errors").TryGetProperty(key, out _));
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var created = await test.CreateOrderAsync(Input());
        using var anonymous = AuthClient.Create(test.Factory);
        (await anonymous.GetCsrfAsync()).Dispose();

        (HttpMethod Method, string Path)[] requests =
        [
            (HttpMethod.Get, "/api/orders"),
            (HttpMethod.Get, $"/api/orders/{created.Id}"),
            (HttpMethod.Post, "/api/orders"),
            (HttpMethod.Put, $"/api/orders/{created.Id}"),
            (HttpMethod.Post, $"/api/orders/{created.Id}/cancel"),
            (HttpMethod.Post, $"/api/orders/{created.Id}/restore"),
        ];

        foreach (var (method, path) in requests)
        {
            using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative))
            {
                Content = method == HttpMethod.Get ? null : JsonContent.Create(Input(version: created.Version)),
            };
            request.Headers.Add("X-XSRF-TOKEN", anonymous.XsrfToken);
            request.Headers.Add(OrdersTest.IdempotencyKeyHeader, Guid.NewGuid().ToString());
            using var response = await anonymous.Http.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {path} → {response.StatusCode}");
        }

        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Orders"));
    }

    [Fact]
    public async Task Write_without_xsrf_is_rejected()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var created = await test.CreateOrderAsync(Input());

        using var create = await test.SendAsync(HttpMethod.Post, "/api/orders", Input(), withCsrf: false);
        using var edit = await test.SendAsync(HttpMethod.Put, $"/api/orders/{created.Id}", Input(value: "1.00", version: created.Version), withCsrf: false);
        using var cancel = await test.SendAsync(HttpMethod.Post, $"/api/orders/{created.Id}/cancel", new { version = created.Version }, withCsrf: false);

        foreach (var response in new[] { create, edit, cancel })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(AuthProblems.CsrfInvalidCode, (await Json(response)).GetProperty("code").GetString());
        }

        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Orders"));
        Assert.Equal("1234.50", (await test.GetJsonAsync($"/api/orders/{created.Id}")).GetProperty("value").GetString());
    }

    [Fact]
    public async Task Create_does_not_create_money_records_or_payment_state()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);

        using var response = await test.CreateAsync(Input());
        var id = (await Json(response)).GetProperty("id").GetGuid();
        var details = await test.GetJsonAsync($"/api/orders/{id}");
        var list = await test.GetJsonAsync("/api/orders");

        var tablesWithRows = await test.TablesWithRowsAsync();
        Assert.DoesNotContain(tablesWithRows, t => !t.StartsWith("access.", StringComparison.Ordinal)
            && !t.EndsWith(".__EFMigrationsHistory", StringComparison.Ordinal)
            && t is not ("orders.Orders" or "orders.OrderNumberCounters" or "reporting.AuditEntries"));
        string[] properties =
        [
            .. details.EnumerateObject().Select(p => p.Name),
            .. list.GetProperty("items")[0].EnumerateObject().Select(p => p.Name),
        ];
        Assert.DoesNotContain(properties, p => p.Contains("paid", StringComparison.OrdinalIgnoreCase) || p.Contains("payment", StringComparison.OrdinalIgnoreCase));
    }

    private static object Input(
        string value = "1234.50",
        string orderDate = "2026-10-28",
        string channel = "olx",
        string? status = null,
        string name = "Jan Testowy",
        string? taxId = "1234563218",
        string postalCode = "00-950",
        string? comment = "Komentarz",
        string[]? suppliers = null,
        string? version = null) => new
        {
            orderDate,
            channel,
            value,
            // W PUT status jest wymagany; domyślnie „W trakcie”, jak przy utworzeniu.
            fulfillmentStatus = status ?? (version is null ? null : "inProgress"),
            customer = new
            {
                kind = "person",
                name,
                phone = "500 600 700",
                email = "jan.testowy@example.com",
                street = (string?)null,
                postalCode,
                city = "Warszawa",
                taxId,
            },
            operational = new
            {
                suppliers = suppliers ?? ["Dostawca A"],
                zoReferences = new[] { "ZO-1" },
                invoiceNumber = (string?)null,
                hasPhotos = false,
                hasReview = true,
                notes = (string?)null,
            },
            comment,
            version,
        };

    /// <summary>Edycja zamówienia ze sklepu zasianego przez <see cref="OrdersTest.SeedShopOrderAsync"/>.</summary>
    private static object ShopInput(
        string version,
        string value = "250.00",
        string orderDate = "2026-10-20",
        string channel = "shop",
        string name = "Sklepowy Klient Testowy",
        string? comment = null) => new
        {
            orderDate,
            channel,
            value,
            fulfillmentStatus = "inProgress",
            customer = new { kind = "person", name },
            operational = new { suppliers = Array.Empty<string>(), zoReferences = Array.Empty<string>(), hasPhotos = false, hasReview = false },
            comment,
            version,
        };

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await AuthClient.ProblemAsync(response);

    private static IReadOnlyList<AuditChangeRow> Changes(AuditRow row) =>
    [
        .. JsonDocument.Parse(row.Changes).RootElement.EnumerateArray().Select(c => new AuditChangeRow(
            c.GetProperty("field").GetString()!,
            c.GetProperty("old").GetString(),
            c.GetProperty("new").GetString(),
            c.GetProperty("personal").GetBoolean())),
    ];

    private sealed record CreatedOrder(Guid Id, string Number, string Version);

    private sealed record AuditRow(string Operation, string AuthorKind, string Label, DateTime BusinessDate, string Changes);

    private sealed record AuditChangeRow(string Field, string? Old, string? New, bool Personal);

    /// <summary>Baza testu z migracjami, zalogowany właściciel i pomocnicze wywołania API.</summary>
    private sealed class OrdersTest : IAsyncDisposable
    {
        public const string IdempotencyKeyHeader = "Idempotency-Key";

        private readonly string _connectionString;

        private OrdersTest(StoneFlowApiFactory factory, AuthClient client, string connectionString)
        {
            Factory = factory;
            Client = client;
            _connectionString = connectionString;
        }

        public StoneFlowApiFactory Factory { get; }

        public AuthClient Client { get; }

        public static async Task<OrdersTest> CreateAsync(SqlServerFixture sqlServer)
        {
            var connectionString = sqlServer.NewDatabase("orders");
            var factory = new StoneFlowApiFactory().WithDatabase(connectionString);
            await TestDatabase.MigrateAsync(factory);
            var client = AuthClient.Create(factory);
            await client.SetUpOwnerAsync(factory);
            using var login = await client.LoginAsync();
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
            return new OrdersTest(factory, client, connectionString);
        }

        public async Task<HttpResponseMessage> SendAsync(
            HttpMethod method, string path, object? body = null, string? idempotencyKey = null, bool withCsrf = true)
        {
            using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative))
            {
                Content = body is null ? null : JsonContent.Create(body),
            };
            if (withCsrf && Client.XsrfToken is not null)
            {
                request.Headers.Add("X-XSRF-TOKEN", Client.XsrfToken);
            }

            if (idempotencyKey is not null)
            {
                request.Headers.Add(IdempotencyKeyHeader, idempotencyKey);
            }

            return await Client.Http.SendAsync(request, TestContext.Current.CancellationToken);
        }

        public Task<HttpResponseMessage> CreateAsync(object body, Guid? key = null) =>
            SendAsync(HttpMethod.Post, "/api/orders", body, (key ?? Guid.NewGuid()).ToString());

        public async Task<CreatedOrder> CreateOrderAsync(object body, Guid? key = null)
        {
            using var response = await CreateAsync(body, key);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var json = await Json(response);
            return new CreatedOrder(json.GetProperty("id").GetGuid(), json.GetProperty("number").GetString()!, json.GetProperty("version").GetString()!);
        }

        /// <summary>Udany <c>PUT</c>; zwraca nową wersję.</summary>
        public async Task<string> EditAsync(Guid id, object body)
        {
            using var response = await SendAsync(HttpMethod.Put, $"/api/orders/{id}", body);
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            return (await Json(response)).GetProperty("version").GetString()!;
        }

        /// <summary>Udane <c>cancel</c> albo <c>restore</c>; zwraca nową wersję.</summary>
        public async Task<string> StatusActionAsync(Guid id, string action, string version)
        {
            using var response = await SendAsync(HttpMethod.Post, $"/api/orders/{id}/{action}", new { version });
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            return (await Json(response)).GetProperty("version").GetString()!;
        }

        public async Task<JsonElement> GetJsonAsync(string path)
        {
            using var response = await SendAsync(HttpMethod.Get, path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            return await Json(response);
        }

        public async Task<Guid[]> IdsAsync(string path) =>
            [.. (await GetJsonAsync(path)).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())];

        /// <summary>Zamówienie ze sklepu wstawione bezpośrednio (import WooCommerce powstanie w SF-12).</summary>
        public async Task<Guid> SeedShopOrderAsync()
        {
            var id = Guid.NewGuid();
            await ExecuteSqlAsync(
                "INSERT INTO orders.Orders (Id, Number, Source, ExternalId, Channel, OrderDate, Value, FulfillmentStatus, ShopStatus, "
                + "CustomerKind, CustomerName, Suppliers, ZoReferences, HasPhotos, HasReview, CreatedAtUtc, UpdatedAtUtc) "
                + $"VALUES ('{id}', N'ZR-2026-9001', N'WooCommerce', N'1001', N'Shop', '2026-10-20', 250.00, N'InProgress', N'processing', "
                + "N'Person', N'Sklepowy Klient Testowy', N'[]', N'[]', 0, 0, SYSUTCDATETIME(), SYSUTCDATETIME());");
            return id;
        }

        public async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = await OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        }

        public async Task ExecuteSqlAsync(string sql)
        {
            await using var connection = await OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        public async Task<IReadOnlyList<AuditRow>> AuditRowsAsync(Guid orderId)
        {
            await using var connection = await OpenAsync();
            await using var command = new SqlCommand(
                "SELECT Operation, AuthorKind, ObjectLabel, ObjectBusinessDate, Changes FROM reporting.AuditEntries "
                + "WHERE ObjectType = N'Order' AND ObjectId = @id AND AuthorAccountId IS NOT NULL ORDER BY Id",
                connection);
            command.Parameters.AddWithValue("@id", orderId);
            await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            var rows = new List<AuditRow>();
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                rows.Add(new AuditRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetDateTime(3), reader.GetString(4)));
            }

            return rows;
        }

        /// <summary>Tabele z co najmniej jednym wierszem, jako <c>schemat.tabela</c>.</summary>
        public async Task<IReadOnlyList<string>> TablesWithRowsAsync()
        {
            await using var connection = await OpenAsync();
            await using var command = new SqlCommand(
                "SELECT s.name + N'.' + t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id "
                + "JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1) "
                + "GROUP BY s.name, t.name HAVING SUM(p.rows) > 0",
                connection);
            await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            var tables = new List<string>();
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                tables.Add(reader.GetString(0));
            }

            return tables;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
        }

        private async Task<SqlConnection> OpenAsync()
        {
            var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            return connection;
        }
    }
}
