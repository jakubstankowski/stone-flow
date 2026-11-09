using System.Net;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Access.Api;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Expenses.Api;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Domain.OwnerWithdrawals;

namespace StoneFlow.Api.IntegrationTests.Expenses;

/// <summary>
/// SF-17: wypłaty właścicieli przez API na prawdziwej bazie — idempotencja, kontrola wersji,
/// usunięcie ze śladem, audyt, ograniczenia bazy i ochrona dostępu. Osoby są fikcyjne.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class OwnerWithdrawalsApiTests(SqlServerFixture sqlServer)
{
    private const string Path = "/api/owner-withdrawals";

    private static Dictionary<string, object?> Input(
        string? person = "Właściciel A",
        string? amount = "2000.00",
        string? withdrawnOn = "2026-11-04",
        string? method = "transfer",
        string? version = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["person"] = person,
            ["amount"] = amount,
            ["withdrawnOn"] = withdrawnOn,
            ["method"] = method,
        };
        if (version is not null)
        {
            body["version"] = version;
        }

        return body;
    }

    [Fact]
    public async Task Create_get_edit_and_delete_round_trip()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);

        using var response = await CreateAsync(test, Input(person: "  Właściciel A ", amount: "2000"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await Json(response);
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetString()!;
        Assert.Equal($"{Path}/{id}", response.Headers.Location?.OriginalString);

        var details = await test.GetJsonAsync($"{Path}/{id}");
        Assert.Equal(id, details.GetProperty("id").GetGuid());
        Assert.Equal("Właściciel A", details.GetProperty("person").GetString());
        Assert.Equal("2000.00", details.GetProperty("amount").GetString());
        Assert.Equal("2026-11-04", details.GetProperty("withdrawnOn").GetString());
        Assert.Equal("transfer", details.GetProperty("method").GetString());
        Assert.Equal(version, details.GetProperty("version").GetString());
        Assert.EndsWith("Z", details.GetProperty("createdAtUtc").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("Z", details.GetProperty("updatedAtUtc").GetString(), StringComparison.Ordinal);

        test.Time.Advance(TimeSpan.FromMinutes(5));
        var edited = await test.ChangeAsync(HttpMethod.Put, $"{Path}/{id}", Input(amount: "1500.50", method: "cash", version: version));
        Assert.NotEqual(version, edited);
        var afterEdit = await test.GetJsonAsync($"{Path}/{id}");
        Assert.Equal("1500.50", afterEdit.GetProperty("amount").GetString());
        Assert.Equal("cash", afterEdit.GetProperty("method").GetString());
        Assert.NotEqual(afterEdit.GetProperty("createdAtUtc").GetString(), afterEdit.GetProperty("updatedAtUtc").GetString());

        using var deleted = await test.SendAsync(HttpMethod.Delete, $"{Path}/{id}", new { version = edited });
        using var get = await test.SendAsync(HttpMethod.Get, $"{Path}/{id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(OwnerWithdrawalProblems.NotFoundCode, await CodeAsync(get, HttpStatusCode.NotFound));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.OwnerWithdrawals"));
    }

    [Fact]
    public async Task Unknown_withdrawal_returns_404_for_every_route()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        var id = Guid.NewGuid();
        var version = Convert.ToBase64String(new byte[8]);

        using var get = await test.SendAsync(HttpMethod.Get, $"{Path}/{id}");
        using var put = await test.SendAsync(HttpMethod.Put, $"{Path}/{id}", Input(version: version));
        using var delete = await test.SendAsync(HttpMethod.Delete, $"{Path}/{id}", new { version });

        Assert.Equal(OwnerWithdrawalProblems.NotFoundCode, await CodeAsync(get, HttpStatusCode.NotFound));
        Assert.Equal(OwnerWithdrawalProblems.NotFoundCode, await CodeAsync(put, HttpStatusCode.NotFound));
        Assert.Equal(OwnerWithdrawalProblems.NotFoundCode, await CodeAsync(delete, HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task Parallel_creates_with_the_same_key_create_one_withdrawal()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        var key = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => CreateAsync(test, Input(), key)));
        try
        {
            Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, r.StatusCode.ToString()));
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            var ids = await Task.WhenAll(responses.Select(async r => (await Json(r)).GetProperty("id").GetGuid()));
            Assert.Single(ids.Distinct());
            Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.OwnerWithdrawals"));
            Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM reporting.AuditEntries WHERE ObjectType = N'OwnerWithdrawal'"));
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
    public async Task Repeated_create_returns_200_and_different_data_with_the_same_key_returns_409()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        var key = Guid.NewGuid();
        var created = await CreateWithdrawalAsync(test, Input(), key);

        // Te same dane po normalizacji: spacje i zapis kwoty.
        using var repeated = await CreateAsync(test, Input(person: " Właściciel A  ", amount: "2000.0"), key);
        using var reused = await CreateAsync(test, Input(amount: "99.00"), key);

        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        var repeatedBody = await Json(repeated);
        Assert.Equal(created.Id, repeatedBody.GetProperty("id").GetGuid());
        Assert.Equal(created.Version, repeatedBody.GetProperty("version").GetString());
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        var problem = await Json(reused);
        Assert.Equal(OwnerWithdrawalProblems.IdempotencyKeyReusedCode, problem.GetProperty("code").GetString());
        Assert.Equal(created.Id, problem.GetProperty("ownerWithdrawalId").GetGuid());
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.OwnerWithdrawals"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nie-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Create_without_a_valid_idempotency_key_returns_400(string? key)
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);

        using var response = await test.SendAsync(HttpMethod.Post, Path, Input(), idempotencyKey: key);

        Assert.Equal(OwnerWithdrawalProblems.IdempotencyKeyRequiredCode, await CodeAsync(response, HttpStatusCode.BadRequest));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.OwnerWithdrawals"));
    }

    [Fact]
    public async Task Invalid_fields_return_400_with_json_keys()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);

        using var empty = await CreateAsync(test, Input(person: " ", amount: null, withdrawnOn: null, method: null));
        using var invalid = await CreateAsync(test, Input(person: new string('a', 101), amount: "0.00", withdrawnOn: "2026-11-06", method: "cashOnDelivery"));
        using var scale = await CreateAsync(test, Input(amount: "10.005"));
        using var negative = await CreateAsync(test, Input(amount: "-100.00"));

        foreach (var response in new[] { empty, invalid })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var errors = (await Json(response)).GetProperty("errors");
            Assert.Equal(["amount", "method", "person", "withdrawnOn"], errors.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        }

        Assert.True((await Json(scale)).GetProperty("errors").TryGetProperty("amount", out _));
        Assert.True((await Json(negative)).GetProperty("errors").TryGetProperty("amount", out _));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.OwnerWithdrawals"));
    }

    [Fact]
    public async Task Edit_validates_version_and_fields()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        var created = await CreateWithdrawalAsync(test, Input());

        using var noVersion = await test.SendAsync(HttpMethod.Put, $"{Path}/{created.Id}", Input());
        using var badAmount = await test.SendAsync(HttpMethod.Put, $"{Path}/{created.Id}", Input(amount: "0", version: created.Version));
        using var deleteWithoutVersion = await test.SendAsync(HttpMethod.Delete, $"{Path}/{created.Id}", new { });

        Assert.True((await Json(noVersion)).GetProperty("errors").TryGetProperty("version", out _));
        Assert.True((await Json(badAmount)).GetProperty("errors").TryGetProperty("amount", out _));
        Assert.True((await Json(deleteWithoutVersion)).GetProperty("errors").TryGetProperty("version", out _));
        Assert.All(new[] { noVersion, badAmount, deleteWithoutVersion }, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
    }

    [Fact]
    public async Task Withdrawal_date_today_is_judged_in_Warsaw_time_and_dates_before_launch_are_accepted()
    {
        // 5.11.2026 23:30 UTC to już 6.11.2026 w Polsce.
        await using var test = await ExpensesTest.CreateAsync(sqlServer, new DateTimeOffset(2026, 11, 5, 23, 30, 0, TimeSpan.Zero));

        using var today = await CreateAsync(test, Input(withdrawnOn: "2026-11-06"));
        using var tomorrow = await CreateAsync(test, Input(withdrawnOn: "2026-11-07"));
        using var beforeLaunch = await CreateAsync(test, Input(withdrawnOn: "2026-10-15"));

        Assert.Equal(HttpStatusCode.Created, today.StatusCode);
        Assert.True((await Json(tomorrow)).GetProperty("errors").TryGetProperty("withdrawnOn", out _));
        Assert.Equal(HttpStatusCode.Created, beforeLaunch.StatusCode);
    }

    [Fact]
    public async Task Every_change_is_audited_without_the_person_in_the_label()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        var created = await CreateWithdrawalAsync(test, Input());

        var edited = await test.ChangeAsync(HttpMethod.Put, $"{Path}/{created.Id}", Input(person: "Wspólnik B", withdrawnOn: "2026-11-03", version: created.Version));
        using var deleted = await test.SendAsync(HttpMethod.Delete, $"{Path}/{created.Id}", new { version = edited });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var rows = await test.AuditRowsAsync(created.Id, "OwnerWithdrawal");
        Assert.Equal(["Created", "Updated", "Deleted"], rows.Select(r => r.Operation));
        Assert.All(rows, r => Assert.Equal("Wypłata właściciela", r.Label));
        Assert.All(rows, r => Assert.Equal("Account", r.AuthorKind));
        Assert.Equal(
            [new DateTime(2026, 11, 4), new DateTime(2026, 11, 3), new DateTime(2026, 11, 3)],
            rows.Select(r => r.BusinessDate));
        Assert.Equal(
            [
                ("person", null, "Właściciel A", true),
                ("amount", null, "2000.00", false),
                ("withdrawnOn", null, "2026-11-04", false),
                ("method", null, "transfer", false),
            ],
            rows[0].ChangeList);
        Assert.Equal(
            [("person", "Właściciel A", "Wspólnik B", true), ("withdrawnOn", "2026-11-04", "2026-11-03", false)],
            rows[1].ChangeList);
        Assert.Equal(
            [
                ("person", "Wspólnik B", null, true),
                ("amount", "2000.00", null, false),
                ("withdrawnOn", "2026-11-03", null, false),
                ("method", "transfer", null, false),
            ],
            rows[2].ChangeList);
        Assert.Equal(1, await test.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM reporting.AuditEntries WHERE ObjectId = '{created.Id}' AND ParentObjectType IS NULL AND ObjectType = N'OwnerWithdrawal' AND Operation = N'Deleted'"));
    }

    [Fact]
    public async Task Edit_without_changes_keeps_the_version_timestamp_and_writes_no_audit()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        var created = await CreateWithdrawalAsync(test, Input());
        var before = await test.GetJsonAsync($"{Path}/{created.Id}");
        test.Time.Advance(TimeSpan.FromMinutes(5));

        var unchanged = await test.ChangeAsync(HttpMethod.Put, $"{Path}/{created.Id}", Input(person: " Właściciel A ", amount: "2000", version: created.Version));

        Assert.Equal(created.Version, unchanged);
        var after = await test.GetJsonAsync($"{Path}/{created.Id}");
        Assert.Equal(before.GetProperty("updatedAtUtc").GetString(), after.GetProperty("updatedAtUtc").GetString());
        Assert.Equal(created.Version, after.GetProperty("version").GetString());
        Assert.Equal(["Created"], (await test.AuditRowsAsync(created.Id, "OwnerWithdrawal")).Select(r => r.Operation));
    }

    [Fact]
    public async Task Stale_version_returns_409_for_edit_and_delete()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        var created = await CreateWithdrawalAsync(test, Input());
        await test.ChangeAsync(HttpMethod.Put, $"{Path}/{created.Id}", Input(amount: "2100.00", version: created.Version));

        using var staleEdit = await test.SendAsync(HttpMethod.Put, $"{Path}/{created.Id}", Input(amount: "1.00", version: created.Version));
        using var staleDelete = await test.SendAsync(HttpMethod.Delete, $"{Path}/{created.Id}", new { version = created.Version });

        Assert.Equal(OwnerWithdrawalProblems.ConflictCode, await CodeAsync(staleEdit, HttpStatusCode.Conflict));
        Assert.Equal(OwnerWithdrawalProblems.ConflictCode, await CodeAsync(staleDelete, HttpStatusCode.Conflict));
        Assert.Equal("2100.00", (await test.GetJsonAsync($"{Path}/{created.Id}")).GetProperty("amount").GetString());
    }

    [Fact]
    public async Task Change_between_load_and_save_or_delete_returns_409_without_audit()
    {
        var hooks = new OwnerWithdrawalStoreHooks();
        await using var test = await ExpensesTest.CreateAsync(sqlServer, configureServices: services => HookedOwnerWithdrawalStore.Register(services, hooks));
        var created = await CreateWithdrawalAsync(test, Input());
        Task ChangeFromAnotherTab(OwnerWithdrawal withdrawal) => test.ExecuteSqlAsync(
            $"UPDATE expenses.OwnerWithdrawals SET Person = N'Zmiana z innej karty' WHERE Id = '{withdrawal.Id}';");
        hooks.BeforeSave = ChangeFromAnotherTab;
        hooks.BeforeDelete = ChangeFromAnotherTab;

        using var edit = await test.SendAsync(HttpMethod.Put, $"{Path}/{created.Id}", Input(amount: "1.00", version: created.Version));
        var afterEdit = (await test.GetJsonAsync($"{Path}/{created.Id}")).GetProperty("version").GetString();
        using var delete = await test.SendAsync(HttpMethod.Delete, $"{Path}/{created.Id}", new { version = afterEdit });

        Assert.Equal(OwnerWithdrawalProblems.ConflictCode, await CodeAsync(edit, HttpStatusCode.Conflict));
        Assert.Equal(OwnerWithdrawalProblems.ConflictCode, await CodeAsync(delete, HttpStatusCode.Conflict));
        var details = await test.GetJsonAsync($"{Path}/{created.Id}");
        Assert.Equal("2000.00", details.GetProperty("amount").GetString());
        Assert.Equal("Zmiana z innej karty", details.GetProperty("person").GetString());
        Assert.Equal(["Created"], (await test.AuditRowsAsync(created.Id, "OwnerWithdrawal")).Select(r => r.Operation));
    }

    [Fact]
    public async Task Audit_failure_rolls_back_the_withdrawal_change()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        var created = await CreateWithdrawalAsync(test, Input());
        await test.ExecuteSqlAsync("ALTER TABLE reporting.AuditEntries WITH NOCHECK ADD CONSTRAINT CK_Test_RejectAll CHECK (ObjectLabel = N'nigdy');");

        using var create = await CreateAsync(test, Input());
        using var edit = await test.SendAsync(HttpMethod.Put, $"{Path}/{created.Id}", Input(amount: "1.00", version: created.Version));
        using var delete = await test.SendAsync(HttpMethod.Delete, $"{Path}/{created.Id}", new { version = created.Version });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, create.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, edit.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, delete.StatusCode);
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.OwnerWithdrawals"));
        Assert.Equal(2000.00m, await test.ScalarAsync<decimal>("SELECT Amount FROM expenses.OwnerWithdrawals"));
    }

    [Fact]
    public async Task Withdrawal_is_not_an_expense()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);

        await CreateWithdrawalAsync(test, Input());

        Assert.Empty((await test.GetJsonAsync("/api/expenses")).GetProperty("items").EnumerateArray());
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.Expenses"));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM reporting.AuditEntries WHERE ObjectType = N'Expense'"));
    }

    [Fact]
    public async Task People_are_unique_ignoring_case_with_the_latest_spelling_in_alphabetical_order()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        Assert.Empty((await test.GetJsonAsync($"{Path}/people")).GetProperty("people").EnumerateArray());

        await CreateWithdrawalAsync(test, Input(person: "wspólnik b"));
        test.Time.Advance(TimeSpan.FromMinutes(1));
        await CreateWithdrawalAsync(test, Input(person: "Właściciel A"));
        test.Time.Advance(TimeSpan.FromMinutes(1));
        await CreateWithdrawalAsync(test, Input(person: "Wspólnik B"));

        var people = (await test.GetJsonAsync($"{Path}/people")).GetProperty("people").EnumerateArray().Select(p => p.GetString()).ToArray();

        Assert.Equal(["Właściciel A", "Wspólnik B"], people);
    }

    [Fact]
    public async Task Database_check_constraints_reject_zero_amount_and_unknown_method()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        Task InsertAsync(string amount, string method) => test.ExecuteSqlAsync(
            "INSERT INTO expenses.OwnerWithdrawals (Id, Person, Amount, WithdrawnOn, Method, CreatedAtUtc, UpdatedAtUtc) "
            + $"VALUES (NEWID(), N'Test', {amount}, '2026-11-04', N'{method}', SYSUTCDATETIME(), SYSUTCDATETIME());");

        await Assert.ThrowsAsync<SqlException>(() => InsertAsync("0", "Cash"));
        await Assert.ThrowsAsync<SqlException>(() => InsertAsync("-5", "Cash"));
        await Assert.ThrowsAsync<SqlException>(() => InsertAsync("10", "CashOnDelivery"));
        await Assert.ThrowsAsync<SqlException>(() => test.ExecuteSqlAsync(
            "INSERT INTO expenses.OwnerWithdrawals (Id, Person, Amount, WithdrawnOn, Method, CreationRequestId, CreatedAtUtc, UpdatedAtUtc) "
            + "VALUES (NEWID(), N'Test', 10, '2026-11-04', N'Cash', NEWID(), SYSUTCDATETIME(), SYSUTCDATETIME());"));
        await InsertAsync("0.01", "Blik");

        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.OwnerWithdrawals"));
    }

    [Fact]
    public async Task Anonymous_requests_and_writes_without_csrf_are_rejected()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        var created = await CreateWithdrawalAsync(test, Input());
        using var anonymous = AuthClient.Create(test.Factory);
        (await anonymous.GetCsrfAsync()).Dispose();

        (HttpMethod Method, string Path)[] requests =
        [
            (HttpMethod.Get, $"{Path}/people"),
            (HttpMethod.Get, $"{Path}/{created.Id}"),
            (HttpMethod.Post, Path),
            (HttpMethod.Put, $"{Path}/{created.Id}"),
            (HttpMethod.Delete, $"{Path}/{created.Id}"),
        ];

        foreach (var (method, path) in requests)
        {
            using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
            request.Headers.Add("X-XSRF-TOKEN", anonymous.XsrfToken);
            using var response = await anonymous.Http.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {path} → {response.StatusCode}");
        }

        using var create = await test.SendAsync(HttpMethod.Post, Path, Input(), Guid.NewGuid().ToString(), withCsrf: false);
        using var edit = await test.SendAsync(HttpMethod.Put, $"{Path}/{created.Id}", Input(amount: "1.00", version: created.Version), withCsrf: false);
        using var delete = await test.SendAsync(HttpMethod.Delete, $"{Path}/{created.Id}", new { version = created.Version }, withCsrf: false);
        foreach (var response in new[] { create, edit, delete })
        {
            Assert.Equal(AuthProblems.CsrfInvalidCode, await CodeAsync(response, HttpStatusCode.BadRequest));
        }

        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.OwnerWithdrawals"));
        Assert.Equal("2000.00", (await test.GetJsonAsync($"{Path}/{created.Id}")).GetProperty("amount").GetString());
    }

    private static Task<HttpResponseMessage> CreateAsync(ExpensesTest test, object body, Guid? key = null) =>
        test.SendAsync(HttpMethod.Post, Path, body, (key ?? Guid.NewGuid()).ToString());

    private static async Task<CreatedExpense> CreateWithdrawalAsync(ExpensesTest test, object body, Guid? key = null)
    {
        using var response = await CreateAsync(test, body, key);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var json = await Json(response);
        return new CreatedExpense(json.GetProperty("id").GetGuid(), json.GetProperty("version").GetString()!);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await AuthClient.ProblemAsync(response);

    private static async Task<string?> CodeAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        return (await Json(response)).GetProperty("code").GetString();
    }
}

/// <summary>Akcje wykonywane przez dekorator portu zapisu tuż przed zapisem albo usunięciem wypłaty.</summary>
internal sealed class OwnerWithdrawalStoreHooks
{
    public Func<OwnerWithdrawal, Task>? BeforeSave { get; set; }

    public Func<OwnerWithdrawal, Task>? BeforeDelete { get; set; }
}

/// <summary>Dekorator prawdziwego <see cref="IOwnerWithdrawalStore"/> (tylko w testach).</summary>
internal sealed class HookedOwnerWithdrawalStore(IOwnerWithdrawalStore inner, OwnerWithdrawalStoreHooks hooks) : IOwnerWithdrawalStore
{
    public static void Register(IServiceCollection services, OwnerWithdrawalStoreHooks hooks)
    {
        var descriptor = services.Single(d => d.ServiceType == typeof(IOwnerWithdrawalStore));
        var implementation = descriptor.ImplementationType
            ?? throw new InvalidOperationException("Port zapisu wypłat powinien być zarejestrowany typem.");
        services.Remove(descriptor);
        services.AddScoped<IOwnerWithdrawalStore>(provider =>
            new HookedOwnerWithdrawalStore((IOwnerWithdrawalStore)ActivatorUtilities.CreateInstance(provider, implementation), hooks));
    }

    public Task<StoredOwnerWithdrawal?> LoadAsync(Guid id, CancellationToken cancellationToken) => inner.LoadAsync(id, cancellationToken);

    public Task<StoreSaveResult> AddAsync(OwnerWithdrawal withdrawal, CancellationToken cancellationToken) => inner.AddAsync(withdrawal, cancellationToken);

    public async Task<StoreSaveResult> SaveAsync(OwnerWithdrawal withdrawal, byte[] expectedVersion, CancellationToken cancellationToken)
    {
        if (hooks.BeforeSave is { } beforeSave)
        {
            await beforeSave(withdrawal);
        }

        return await inner.SaveAsync(withdrawal, expectedVersion, cancellationToken);
    }

    public async Task<StoreSaveResult> DeleteAsync(OwnerWithdrawal withdrawal, byte[] expectedVersion, CancellationToken cancellationToken)
    {
        if (hooks.BeforeDelete is { } beforeDelete)
        {
            await beforeDelete(withdrawal);
        }

        return await inner.DeleteAsync(withdrawal, expectedVersion, cancellationToken);
    }
}
