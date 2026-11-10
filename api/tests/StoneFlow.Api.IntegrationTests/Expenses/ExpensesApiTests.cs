using System.Net;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using StoneFlow.Access.Api;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Expenses.Api;
using StoneFlow.Expenses.Domain.Expenses;

namespace StoneFlow.Api.IntegrationTests.Expenses;

/// <summary>
/// SF-15: wydatki przez API na prawdziwej bazie — idempotencja, kontrola wersji, akcje zapłaty,
/// usunięcie ze śladem, audyt, lista oraz ochrona dostępu. Dane są fikcyjne.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ExpensesApiTests(SqlServerFixture sqlServer)
{
    private static Dictionary<string, object?> Input(
        string? description = "Cięcie płyt",
        string? category = "cutting",
        string? costDate = "2026-10-28",
        string? dueDate = "2026-11-10",
        string? paidOn = null,
        string? grossAmount = "1234.50",
        string? netAmount = "1003.66",
        string? paymentMethod = "transfer",
        string? counterparty = "Dostawca A",
        string? payer = null,
        string? version = null) =>
        ExpensesTest.Input(
            description: description,
            category: category,
            costDate: costDate,
            dueDate: dueDate,
            paidOn: paidOn,
            grossAmount: grossAmount,
            netAmount: netAmount,
            paymentMethod: paymentMethod,
            counterparty: counterparty,
            payer: payer,
            version: version);

    [Fact]
    public async Task Create_returns_201_and_the_expense_is_planned_in_details_and_on_the_list()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);

        using var response = await test.CreateAsync(Input(grossAmount: "-500.00", netAmount: "-406.50"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await Json(response);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal($"/api/expenses/{id}", response.Headers.Location?.OriginalString);

        var details = await test.GetJsonAsync($"/api/expenses/{id}");
        Assert.Equal("-500.00", details.GetProperty("grossAmount").GetString());
        Assert.Equal("-406.50", details.GetProperty("netAmount").GetString());
        Assert.Equal("planned", details.GetProperty("paymentStatus").GetString());
        Assert.False(details.GetProperty("overdue").GetBoolean());
        Assert.Equal(JsonValueKind.Null, details.GetProperty("paidOn").ValueKind);
        Assert.EndsWith("Z", details.GetProperty("createdAtUtc").GetString(), StringComparison.Ordinal);

        var item = Assert.Single((await test.GetJsonAsync("/api/expenses")).GetProperty("items").EnumerateArray());
        Assert.Equal("planned", item.GetProperty("paymentStatus").GetString());
        Assert.Equal("cutting", item.GetProperty("category").GetString());
        Assert.Equal("2026-11-10", item.GetProperty("dueDate").GetString());
    }

    [Fact]
    public async Task Create_does_not_create_any_money_record_besides_the_expense_and_its_audit()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);

        await test.CreateExpenseAsync(Input());

        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.Expenses"));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.Expenses WHERE PaidOn IS NOT NULL"));
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM reporting.AuditEntries WHERE ObjectType = N'Expense'"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nie-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Create_without_a_valid_idempotency_key_returns_400(string? key)
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);

        using var response = await test.SendAsync(HttpMethod.Post, "/api/expenses", Input(), idempotencyKey: key);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ExpenseProblems.IdempotencyKeyRequiredCode, (await Json(response)).GetProperty("code").GetString());
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.Expenses"));
    }

    [Fact]
    public async Task Repeated_create_returns_200_and_different_data_with_the_same_key_returns_409()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        var key = Guid.NewGuid();
        var created = await test.CreateExpenseAsync(Input(), key);

        // Te same dane po normalizacji: spacje i zapis kwoty.
        using var repeated = await test.CreateAsync(Input(description: "  Cięcie płyt ", grossAmount: "1234.5"), key);
        using var reused = await test.CreateAsync(Input(grossAmount: "99.00", netAmount: null), key);

        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(created.Id, (await Json(repeated)).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        var problem = await Json(reused);
        Assert.Equal(ExpenseProblems.IdempotencyKeyReusedCode, problem.GetProperty("code").GetString());
        Assert.Equal(created.Id, problem.GetProperty("expenseId").GetGuid());
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.Expenses"));
    }

    [Fact]
    public async Task Parallel_creates_with_the_same_key_create_one_expense()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        var key = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => test.CreateAsync(Input(), key)));
        try
        {
            Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, r.StatusCode.ToString()));
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.Expenses"));
            Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM reporting.AuditEntries WHERE ObjectType = N'Expense'"));
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
    public async Task Invalid_amounts_and_future_payment_date_return_400_with_json_keys()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);

        using var zero = await test.CreateAsync(Input(grossAmount: "0.00", netAmount: null));
        using var sign = await test.CreateAsync(Input(grossAmount: "-500.00", netAmount: "406.50"));
        using var larger = await test.CreateAsync(Input(grossAmount: "100.00", netAmount: "120.00"));
        using var future = await test.CreateAsync(Input(paidOn: "2026-11-06"));
        using var scale = await test.CreateAsync(Input(grossAmount: "10.005", netAmount: null));

        Assert.True((await Json(zero)).GetProperty("errors").TryGetProperty("grossAmount", out _));
        Assert.True((await Json(sign)).GetProperty("errors").TryGetProperty("netAmount", out _));
        Assert.True((await Json(larger)).GetProperty("errors").TryGetProperty("netAmount", out _));
        Assert.True((await Json(future)).GetProperty("errors").TryGetProperty("paidOn", out _));
        Assert.True((await Json(scale)).GetProperty("errors").TryGetProperty("grossAmount", out _));
        Assert.All(new[] { zero, sign, larger, future, scale }, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.Expenses"));
    }

    [Fact]
    public async Task Zero_net_amount_is_accepted()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);

        var created = await test.CreateExpenseAsync(Input(grossAmount: "-230.00", netAmount: "0"));

        Assert.Equal("0.00", (await test.GetJsonAsync($"/api/expenses/{created.Id}")).GetProperty("netAmount").GetString());
    }

    [Fact]
    public async Task Cost_date_before_launch_with_later_payment_date_is_accepted()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);

        var created = await test.CreateExpenseAsync(Input(costDate: "2020-01-15", dueDate: null, paidOn: "2026-11-05"));

        var details = await test.GetJsonAsync($"/api/expenses/{created.Id}");
        Assert.Equal("2020-01-15", details.GetProperty("costDate").GetString());
        Assert.Equal("paid", details.GetProperty("paymentStatus").GetString());
    }

    [Fact]
    public async Task Payment_date_today_is_judged_in_Warsaw_time()
    {
        // 5.11.2026 23:30 UTC to już 6.11.2026 w Polsce.
        await using var test = await ExpensesTest.CreateAsync(sqlServer, new DateTimeOffset(2026, 11, 5, 23, 30, 0, TimeSpan.Zero));

        using var accepted = await test.CreateAsync(Input(paidOn: "2026-11-06"));
        using var rejected = await test.CreateAsync(Input(paidOn: "2026-11-07"));

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Fact]
    public async Task Mark_paid_and_unmark_change_the_state_and_write_audit()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        var created = await test.CreateExpenseAsync(Input());

        var paid = await test.ChangeAsync(HttpMethod.Post, $"/api/expenses/{created.Id}/mark-paid", new { paidOn = "2026-11-04", paymentMethod = "blik", version = created.Version });
        var paidDetails = await test.GetJsonAsync($"/api/expenses/{created.Id}");
        var unmarked = await test.ChangeAsync(HttpMethod.Post, $"/api/expenses/{created.Id}/unmark-paid", new { version = paid });
        var plannedDetails = await test.GetJsonAsync($"/api/expenses/{created.Id}");

        Assert.Equal("paid", paidDetails.GetProperty("paymentStatus").GetString());
        Assert.Equal("2026-11-04", paidDetails.GetProperty("paidOn").GetString());
        Assert.Equal("blik", paidDetails.GetProperty("paymentMethod").GetString());
        Assert.Equal("planned", plannedDetails.GetProperty("paymentStatus").GetString());
        Assert.Equal("blik", plannedDetails.GetProperty("paymentMethod").GetString());
        Assert.NotEqual(paid, unmarked);

        var rows = await test.AuditRowsAsync(created.Id);
        Assert.Equal(["Created", "Updated", "Updated"], rows.Select(r => r.Operation));
        Assert.Equal([("paidOn", null, "2026-11-04", false), ("paymentMethod", "transfer", "blik", false)], rows[1].ChangeList);
        Assert.Equal([("paidOn", "2026-11-04", null, false)], rows[2].ChangeList);
    }

    [Fact]
    public async Task Mark_paid_rejects_a_future_date()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        var created = await test.CreateExpenseAsync(Input());

        using var response = await test.SendAsync(HttpMethod.Post, $"/api/expenses/{created.Id}/mark-paid", new { paidOn = "2026-11-06", version = created.Version });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await Json(response)).GetProperty("errors").TryGetProperty("paidOn", out _));
        Assert.Equal("planned", (await test.GetJsonAsync($"/api/expenses/{created.Id}")).GetProperty("paymentStatus").GetString());
    }

    [Fact]
    public async Task Stale_version_gives_conflict_before_the_payment_rule_and_current_version_gives_the_rule()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        var created = await test.CreateExpenseAsync(Input());
        var paid = await test.ChangeAsync(HttpMethod.Post, $"/api/expenses/{created.Id}/mark-paid", new { paidOn = "2026-11-04", version = created.Version });

        // Druga karta z wersją sprzed oznaczenia zapłaty.
        using var staleMark = await test.SendAsync(HttpMethod.Post, $"/api/expenses/{created.Id}/mark-paid", new { paidOn = "2026-11-04", version = created.Version });
        using var currentMark = await test.SendAsync(HttpMethod.Post, $"/api/expenses/{created.Id}/mark-paid", new { paidOn = "2026-11-04", version = paid });
        var planned = await test.ChangeAsync(HttpMethod.Post, $"/api/expenses/{created.Id}/unmark-paid", new { version = paid });
        using var staleUnmark = await test.SendAsync(HttpMethod.Post, $"/api/expenses/{created.Id}/unmark-paid", new { version = paid });
        using var currentUnmark = await test.SendAsync(HttpMethod.Post, $"/api/expenses/{created.Id}/unmark-paid", new { version = planned });

        Assert.Equal(ExpenseProblems.ConflictCode, await CodeAsync(staleMark, HttpStatusCode.Conflict));
        Assert.Equal(ExpenseProblems.AlreadyPaidCode, await CodeAsync(currentMark, HttpStatusCode.Conflict));
        Assert.Equal(ExpenseProblems.ConflictCode, await CodeAsync(staleUnmark, HttpStatusCode.Conflict));
        Assert.Equal(ExpenseProblems.NotPaidCode, await CodeAsync(currentUnmark, HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Edit_clears_payment_date_without_extra_flag_and_stale_edit_is_rejected()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        var created = await test.CreateExpenseAsync(Input(paidOn: "2026-11-01"));

        var edited = await test.ChangeAsync(HttpMethod.Put, $"/api/expenses/{created.Id}", Input(paidOn: null, version: created.Version));
        using var stale = await test.SendAsync(HttpMethod.Put, $"/api/expenses/{created.Id}", Input(description: "Inny opis", version: created.Version));
        var unchanged = await test.ChangeAsync(HttpMethod.Put, $"/api/expenses/{created.Id}", Input(paidOn: null, version: edited));

        Assert.Equal(ExpenseProblems.ConflictCode, await CodeAsync(stale, HttpStatusCode.Conflict));
        Assert.Equal(edited, unchanged);
        var details = await test.GetJsonAsync($"/api/expenses/{created.Id}");
        Assert.Equal("planned", details.GetProperty("paymentStatus").GetString());
        Assert.Equal("Cięcie płyt", details.GetProperty("description").GetString());
        var rows = await test.AuditRowsAsync(created.Id);
        Assert.Equal(["Created", "Updated"], rows.Select(r => r.Operation));
        Assert.Equal([("paidOn", "2026-11-01", null, false)], rows[1].ChangeList);
    }

    [Fact]
    public async Task Change_between_load_and_save_or_delete_returns_409_expense_conflict()
    {
        // Wiersz zmieniony innym połączeniem już po wczytaniu w jednostce pracy: EF dostaje
        // DbUpdateConcurrencyException, port zwraca konflikt, API — 409 bez zapisu i bez audytu.
        var hooks = new ExpenseStoreHooks();
        await using var test = await ExpensesTest.CreateAsync(sqlServer, hooks: hooks);
        var created = await test.CreateExpenseAsync(Input());
        Task ChangeFromAnotherTab(Expense expense) => test.ExecuteSqlAsync(
            $"UPDATE expenses.Expenses SET Comment = N'Zmiana z innej karty' WHERE Id = '{expense.Id}';");
        hooks.BeforeSave = ChangeFromAnotherTab;
        hooks.BeforeDelete = ChangeFromAnotherTab;

        using var edit = await test.SendAsync(HttpMethod.Put, $"/api/expenses/{created.Id}", Input(description: "Poprawka", version: created.Version));
        var afterEdit = (await test.GetJsonAsync($"/api/expenses/{created.Id}")).GetProperty("version").GetString();
        using var delete = await test.SendAsync(HttpMethod.Delete, $"/api/expenses/{created.Id}", new { version = afterEdit });

        Assert.Equal(ExpenseProblems.ConflictCode, await CodeAsync(edit, HttpStatusCode.Conflict));
        Assert.Equal(ExpenseProblems.ConflictCode, await CodeAsync(delete, HttpStatusCode.Conflict));
        var details = await test.GetJsonAsync($"/api/expenses/{created.Id}");
        Assert.Equal("Cięcie płyt", details.GetProperty("description").GetString());
        Assert.Equal("Zmiana z innej karty", details.GetProperty("comment").GetString());
        Assert.Equal(["Created"], (await test.AuditRowsAsync(created.Id)).Select(r => r.Operation));
    }

    [Fact]
    public async Task Delete_removes_the_row_and_leaves_a_complete_audit_trace()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        var created = await test.CreateExpenseAsync(Input(payer: "Właściciel"));

        using var stale = await test.SendAsync(HttpMethod.Delete, $"/api/expenses/{created.Id}", new { version = Convert.ToBase64String(new byte[8]) });
        using var deleted = await test.SendAsync(HttpMethod.Delete, $"/api/expenses/{created.Id}", new { version = created.Version });
        using var again = await test.SendAsync(HttpMethod.Delete, $"/api/expenses/{created.Id}", new { version = created.Version });
        using var get = await test.SendAsync(HttpMethod.Get, $"/api/expenses/{created.Id}");

        Assert.Equal(ExpenseProblems.ConflictCode, await CodeAsync(stale, HttpStatusCode.Conflict));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(ExpenseProblems.NotFoundCode, await CodeAsync(again, HttpStatusCode.NotFound));
        Assert.Equal(ExpenseProblems.NotFoundCode, await CodeAsync(get, HttpStatusCode.NotFound));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.Expenses"));

        var rows = await test.AuditRowsAsync(created.Id);
        Assert.Equal(["Created", "Deleted"], rows.Select(r => r.Operation));
        Assert.All(rows, r => Assert.Equal("Wydatek – Cięcie", r.Label));
        Assert.All(rows, r => Assert.Equal(new DateTime(2026, 10, 28), r.BusinessDate));
        Assert.All(rows[1].ChangeList, c => Assert.Null(c.New));
        Assert.Contains(("grossAmount", "1234.50", null, false), rows[1].ChangeList);
        Assert.Contains(("payer", "Właściciel", null, true), rows[1].ChangeList);
    }

    [Fact]
    public async Task Audit_failure_rolls_back_the_expense_change()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        var created = await test.CreateExpenseAsync(Input());
        await test.ExecuteSqlAsync("ALTER TABLE reporting.AuditEntries WITH NOCHECK ADD CONSTRAINT CK_Test_RejectAll CHECK (ObjectLabel = N'nigdy');");

        using var create = await test.CreateAsync(Input());
        using var mark = await test.SendAsync(HttpMethod.Post, $"/api/expenses/{created.Id}/mark-paid", new { paidOn = "2026-11-04", version = created.Version });
        using var delete = await test.SendAsync(HttpMethod.Delete, $"/api/expenses/{created.Id}", new { version = created.Version });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, create.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, mark.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, delete.StatusCode);
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.Expenses"));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.Expenses WHERE PaidOn IS NOT NULL"));
    }

    [Fact]
    public async Task Database_check_constraints_reject_invalid_amounts()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        Task InsertAsync(string gross, string net) => test.ExecuteSqlAsync(
            "INSERT INTO expenses.Expenses (Id, Description, Category, CostDate, GrossAmount, NetAmount, CreatedAtUtc, UpdatedAtUtc) "
            + $"VALUES (NEWID(), N'Test', N'Cutting', '2026-10-28', {gross}, {net}, SYSUTCDATETIME(), SYSUTCDATETIME());");

        await Assert.ThrowsAsync<SqlException>(() => InsertAsync("0", "NULL"));
        await Assert.ThrowsAsync<SqlException>(() => InsertAsync("-500", "406.50"));
        await Assert.ThrowsAsync<SqlException>(() => InsertAsync("100", "120"));
        await InsertAsync("-100", "0");

        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.Expenses"));
    }

    [Fact]
    public async Task List_filters_by_cost_date_payment_date_category_state_sign_and_text()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        var october = (await test.CreateExpenseAsync(Input(costDate: "2026-10-05", dueDate: "2026-10-20", counterparty: "Dostawca 50%"))).Id;
        var paid = (await test.CreateExpenseAsync(Input(costDate: "2026-10-28", dueDate: null, paidOn: "2026-11-02", category: "marketing"))).Id;
        var refund = (await test.CreateExpenseAsync(Input(costDate: "2026-11-03", dueDate: "2026-11-30", grossAmount: "-200.00", netAmount: null))).Id;

        Assert.Equal([october], await test.IdsAsync("/api/expenses?costDateTo=2026-10-10"));
        Assert.Equal([refund, paid], await test.IdsAsync("/api/expenses?costDateFrom=2026-10-20"));
        Assert.Equal([paid], await test.IdsAsync("/api/expenses?paidOnFrom=2026-11-01"));
        Assert.Equal([paid], await test.IdsAsync("/api/expenses?category=marketing&category=vatTax"));
        Assert.Equal([refund, october], await test.IdsAsync("/api/expenses?state=planned"));
        Assert.Equal([paid], await test.IdsAsync("/api/expenses?state=paid"));
        Assert.Equal([october], await test.IdsAsync("/api/expenses?state=overdue"));
        Assert.Empty(await test.IdsAsync("/api/expenses?state=planned&paidOnFrom=2026-01-01"));
        Assert.Equal([refund], await test.IdsAsync("/api/expenses?sign=negative"));
        Assert.Equal([paid, october], await test.IdsAsync("/api/expenses?sign=positive"));
        Assert.Equal([october], await test.IdsAsync("/api/expenses?search=50%25"));
        Assert.Empty(await test.IdsAsync("/api/expenses?search=_"));
    }

    [Fact]
    public async Task List_gross_total_covers_the_whole_filter_not_the_page()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        await test.CreateExpenseAsync(Input(costDate: "2026-10-05", grossAmount: "1000.00", netAmount: null));
        await test.CreateExpenseAsync(Input(costDate: "2026-10-28", dueDate: null, paidOn: "2026-11-02", category: "marketing", grossAmount: "250.50", netAmount: null));
        await test.CreateExpenseAsync(Input(costDate: "2026-11-03", grossAmount: "-200.00", netAmount: null));

        async Task<(string? GrossTotal, int Items)> ListAsync(string query)
        {
            var page = await test.GetJsonAsync($"/api/expenses{query}");
            return (page.GetProperty("grossTotal").GetString(), page.GetProperty("items").GetArrayLength());
        }

        Assert.Equal(("1050.50", 3), await ListAsync(string.Empty));
        Assert.Equal(("1050.50", 1), await ListAsync("?pageSize=1"));
        Assert.Equal(("1050.50", 0), await ListAsync("?page=9"));
        Assert.Equal(("800.00", 2), await ListAsync("?category=cutting"));
        Assert.Equal(("250.50", 1), await ListAsync("?state=paid"));
        Assert.Equal(("-200.00", 1), await ListAsync("?costDateFrom=2026-11-01&costDateTo=2026-11-30"));
        Assert.Equal(("0.00", 0), await ListAsync("?costDateFrom=2026-12-01"));
    }

    [Fact]
    public async Task State_filter_matches_the_state_computed_by_the_domain()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        await test.CreateExpenseAsync(Input(dueDate: "2026-11-04"));
        await test.CreateExpenseAsync(Input(dueDate: "2026-11-05"));
        await test.CreateExpenseAsync(Input(dueDate: null));
        await test.CreateExpenseAsync(Input(dueDate: "2026-11-01", paidOn: "2026-11-03"));

        var items = (await test.GetJsonAsync("/api/expenses")).GetProperty("items").EnumerateArray().ToList();
        foreach (var (state, matches) in new (string, Func<JsonElement, bool>)[]
        {
            ("planned", i => i.GetProperty("paymentStatus").GetString() == "planned"),
            ("paid", i => i.GetProperty("paymentStatus").GetString() == "paid"),
            ("overdue", i => i.GetProperty("overdue").GetBoolean()),
        })
        {
            var expected = items.Where(matches).Select(i => i.GetProperty("id").GetGuid()).Order().ToArray();
            var filtered = (await test.IdsAsync($"/api/expenses?state={state}")).Order().ToArray();
            Assert.Equal(expected, filtered);
        }

        Assert.Single(items, i => i.GetProperty("overdue").GetBoolean());
    }

    [Fact]
    public async Task List_sorts_with_empty_dates_last_and_pages_with_total()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        var early = (await test.CreateExpenseAsync(Input(dueDate: "2026-11-01"))).Id;
        var none = (await test.CreateExpenseAsync(Input(dueDate: null))).Id;
        var late = (await test.CreateExpenseAsync(Input(dueDate: "2026-12-01"))).Id;

        Assert.Equal([early, late, none], await test.IdsAsync("/api/expenses?sort=dueDate&direction=asc"));
        Assert.Equal([late, early, none], await test.IdsAsync("/api/expenses?sort=dueDate&direction=desc"));

        var unpaid = (await test.CreateExpenseAsync(Input(dueDate: null, grossAmount: "-50.00", netAmount: null))).Id;
        var paidEarly = (await test.CreateExpenseAsync(Input(dueDate: null, paidOn: "2026-11-01", grossAmount: "10.00", netAmount: null))).Id;
        var paidLate = (await test.CreateExpenseAsync(Input(dueDate: null, paidOn: "2026-11-04", grossAmount: "5000.00", netAmount: null))).Id;
        var unpaidIds = new[] { early, none, late, unpaid }.Order().ToArray();
        var paidAsc = await test.IdsAsync("/api/expenses?sort=paidOn&direction=asc");
        var paidDesc = await test.IdsAsync("/api/expenses?sort=paidOn&direction=desc");
        Assert.Equal([paidEarly, paidLate], paidAsc[..2]);
        Assert.Equal([paidLate, paidEarly], paidDesc[..2]);
        Assert.Equal(unpaidIds, paidAsc[2..].Order().ToArray());
        Assert.Equal(unpaidIds, paidDesc[2..].Order().ToArray());
        var byAmount = await test.IdsAsync("/api/expenses?sort=grossAmount&direction=asc");
        Assert.Equal(unpaid, byAmount[0]);
        Assert.Equal(paidEarly, byAmount[1]);
        Assert.Equal(paidLate, byAmount[^1]);
        Assert.Equal(paidLate, (await test.IdsAsync("/api/expenses?sort=grossAmount&direction=desc"))[0]);

        var page = await test.GetJsonAsync("/api/expenses?pageSize=4&page=2");
        Assert.Equal(6, page.GetProperty("total").GetInt32());
        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        var beyond = await test.GetJsonAsync("/api/expenses?page=50");
        Assert.Equal(6, beyond.GetProperty("total").GetInt32());
        Assert.Empty(beyond.GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("sort=description", "sort")]
    [InlineData("state=unpaid", "state")]
    [InlineData("sign=zero", "sign")]
    [InlineData("category=fuel", "category")]
    [InlineData("costDateFrom=2026-11-02&costDateTo=2026-11-01", "costDateTo")]
    [InlineData("paidOnFrom=2026-11-02&paidOnTo=2026-11-01", "paidOnTo")]
    public async Task Invalid_list_parameters_return_400(string query, string key)
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);

        using var response = await test.SendAsync(HttpMethod.Get, $"/api/expenses?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await Json(response)).GetProperty("errors").TryGetProperty(key, out _));
    }

    [Fact]
    public async Task Anonymous_requests_and_writes_without_csrf_are_rejected()
    {
        await using var test = await ExpensesTest.CreateAsync(sqlServer);
        var created = await test.CreateExpenseAsync(Input());
        using var anonymous = AuthClient.Create(test.Factory);
        (await anonymous.GetCsrfAsync()).Dispose();

        (HttpMethod Method, string Path)[] requests =
        [
            (HttpMethod.Get, "/api/expenses"),
            (HttpMethod.Get, $"/api/expenses/{created.Id}"),
            (HttpMethod.Post, "/api/expenses"),
            (HttpMethod.Put, $"/api/expenses/{created.Id}"),
            (HttpMethod.Post, $"/api/expenses/{created.Id}/mark-paid"),
            (HttpMethod.Post, $"/api/expenses/{created.Id}/unmark-paid"),
            (HttpMethod.Delete, $"/api/expenses/{created.Id}"),
        ];

        foreach (var (method, path) in requests)
        {
            using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
            request.Headers.Add("X-XSRF-TOKEN", anonymous.XsrfToken);
            using var response = await anonymous.Http.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {path} → {response.StatusCode}");
        }

        using var create = await test.SendAsync(HttpMethod.Post, "/api/expenses", Input(), Guid.NewGuid().ToString(), withCsrf: false);
        using var mark = await test.SendAsync(HttpMethod.Post, $"/api/expenses/{created.Id}/mark-paid", new { paidOn = "2026-11-04", version = created.Version }, withCsrf: false);
        using var delete = await test.SendAsync(HttpMethod.Delete, $"/api/expenses/{created.Id}", new { version = created.Version }, withCsrf: false);
        foreach (var response in new[] { create, mark, delete })
        {
            Assert.Equal(AuthProblems.CsrfInvalidCode, await CodeAsync(response, HttpStatusCode.BadRequest));
        }

        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.Expenses"));
        Assert.Equal("planned", (await test.GetJsonAsync($"/api/expenses/{created.Id}")).GetProperty("paymentStatus").GetString());
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await AuthClient.ProblemAsync(response);

    private static async Task<string?> CodeAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        return (await Json(response)).GetProperty("code").GetString();
    }
}
