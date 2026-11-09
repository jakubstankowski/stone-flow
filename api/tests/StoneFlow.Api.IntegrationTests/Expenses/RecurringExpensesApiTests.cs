using System.Net;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Expenses.Api;
using static StoneFlow.Api.IntegrationTests.Expenses.Recurring;

namespace StoneFlow.Api.IntegrationTests.Expenses;

/// <summary>
/// SF-16: wydatki cykliczne przez API na prawdziwej bazie — tworzenie wystąpień przy zapisie,
/// podgląd, idempotencja, kolejność kodów, wstrzymanie i wznowienie, zmiana dnia i zakresu, usunięcie
/// z kaskadą, audyt z rodzicem, kolizje i ograniczenia bazy. Dane fikcyjne.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class RecurringExpensesApiTests(SqlServerFixture sqlServer)
{
    [Fact]
    public async Task Create_with_a_past_start_creates_the_previewed_planned_occurrences_with_audit_in_one_save()
    {
        await using var test = await CreateTestAsync(sqlServer, At(20, 11));

        var preview = await test.PreviewAsync("dayOfMonth=10&startsOn=2026-09-01");
        using var response = await test.PostAsync(Input());

        Assert.Equal(3, preview.GetProperty("occurrencesToCreate").GetInt32());
        Assert.Equal("2026-09-10", preview.GetProperty("firstImmediateDueDate").GetString());
        Assert.Equal(2, preview.GetProperty("beforeLaunchCount").GetInt32());
        Assert.Equal("2026-12-10", preview.GetProperty("nextDueDate").GetString());
        Assert.False(preview.GetProperty("paused").GetBoolean());
        Assert.Equal(0, preview.GetProperty("droppedDueCount").GetInt32());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await AuthClient.ProblemAsync(response);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal($"{Base}/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal(3, body.GetProperty("createdOccurrences").GetInt32());

        Assert.Equal(["2026-09-10", "2026-10-10", "2026-11-10"], await test.OccurrencesAsync(id));
        Assert.Equal(3, await test.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM expenses.Expenses WHERE RecurringDefinitionId = '{id}' AND PaidOn IS NULL AND DueDate = CostDate AND DocumentReference IS NULL AND GrossAmount = 300"));
        Assert.Equal(3, await test.ScalarAsync<int>($"SELECT COUNT(*) FROM expenses.RecurringExpensePeriods WHERE DefinitionId = '{id}'"));
        Assert.Equal(1, await test.ScalarAsync<int>($"SELECT COUNT(*) FROM expenses.RecurringExpenseDays WHERE DefinitionId = '{id}' AND FromPeriod = '2026-09-01' AND DayOfMonth = 10"));

        var definitionAudit = Assert.Single(await AuditAsync(test, "RecurringExpenseDefinition", id));
        Assert.Equal(("Created", "Account", (string?)null), (definitionAudit.Operation, definitionAudit.AuthorKind, definitionAudit.Parent));
        var occurrenceAudits = await test.RowsAsync(
            $"SELECT Operation, AuthorKind, ParentObjectType, ParentObjectId FROM reporting.AuditEntries WHERE ObjectType = N'Expense'",
            r => (r.GetString(0), r.GetString(1), r.GetString(2), r.GetGuid(3)));
        Assert.Equal(3, occurrenceAudits.Count);
        Assert.All(occurrenceAudits, a => Assert.Equal(("Created", "AutomatedJob", "RecurringExpenseDefinition", id), a));

        var items = (await test.GetJsonAsync($"/api/expenses?recurringDefinitionId={id}")).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        Assert.All(items, item =>
        {
            Assert.Equal(id, item.GetProperty("recurringDefinitionId").GetGuid());
            Assert.Equal("planned", item.GetProperty("paymentStatus").GetString());
        });
        var expense = await test.GetJsonAsync($"/api/expenses/{items.Last().GetProperty("id").GetGuid()}");
        var recurring = expense.GetProperty("recurring");
        Assert.Equal(id, recurring.GetProperty("definitionId").GetGuid());
        Assert.Equal("2026-09", recurring.GetProperty("period").GetString());
        Assert.True(recurring.GetProperty("definitionExists").GetBoolean());

        var details = await test.DetailsAsync(id);
        Assert.Equal("active", details.GetProperty("status").GetString());
        Assert.Equal("2026-12-10", details.GetProperty("nextDueDate").GetString());
        Assert.Equal(3, details.GetProperty("occurrenceCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, details.GetProperty("resumePreview").ValueKind);
        Assert.Equal(10, details.GetProperty("dayOfMonth").GetInt32());
        Assert.Equal("300.00", details.GetProperty("grossAmount").GetString());
    }

    [Fact]
    public async Task Create_rejects_a_non_positive_amount_a_range_without_due_date_and_a_missing_key()
    {
        await using var test = await CreateTestAsync(sqlServer, At(20, 11));

        Assert.Equal("grossAmount", await ProblemCodeAsync(await test.PostAsync(Input(grossAmount: "0", netAmount: null)), HttpStatusCode.BadRequest));
        Assert.Equal("grossAmount", await ProblemCodeAsync(await test.PostAsync(Input(grossAmount: "-10.00", netAmount: null)), HttpStatusCode.BadRequest));
        Assert.Equal(
            "endsOn",
            await ProblemCodeAsync(await test.PostAsync(Input(startsOn: "2026-03-15", endsOn: "2026-03-20")), HttpStatusCode.BadRequest));
        Assert.Equal(
            ExpenseProblems.IdempotencyKeyRequiredCode,
            await ProblemCodeAsync(await test.SendAsync(HttpMethod.Post, Base, Input()), HttpStatusCode.BadRequest));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.RecurringExpenseDefinitions"));
        Assert.Equal("endsOn", await ProblemCodeAsync(await test.SendAsync(HttpMethod.Get, $"{Base}/preview?dayOfMonth=10&startsOn=2026-03-15&endsOn=2026-03-20"), HttpStatusCode.BadRequest));
        Assert.Equal("dayOfMonth", await ProblemCodeAsync(await test.SendAsync(HttpMethod.Get, $"{Base}/preview?dayOfMonth=32&startsOn=2026-03-15"), HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Repeated_create_returns_the_stored_count_and_after_deletion_it_is_rejected()
    {
        await using var test = await CreateTestAsync(sqlServer, At(20, 11));
        var key = Guid.NewGuid();
        var created = await test.CreateRecurringAsync(Input(), key);

        using var repeated = await test.PostAsync(Input(description: "  Abonament "), key);
        using var reused = await test.PostAsync(Input(grossAmount: "301.00"), key);

        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        var repeatedBody = await AuthClient.ProblemAsync(repeated);
        Assert.Equal(created.Id, repeatedBody.GetProperty("id").GetGuid());
        Assert.Equal(3, repeatedBody.GetProperty("createdOccurrences").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        var reusedBody = await AuthClient.ProblemAsync(reused);
        Assert.Equal(ExpenseProblems.IdempotencyKeyReusedCode, reusedBody.GetProperty("code").GetString());
        Assert.Equal(created.Id, reusedBody.GetProperty("recurringExpenseId").GetGuid());

        using var deleted = await test.SendAsync(HttpMethod.Delete, $"{Base}/{created.Id}", new Dictionary<string, object?> { ["version"] = created.Version });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.Equal(ExpenseProblems.RecurringDeletedCode, await ProblemCodeAsync(await test.PostAsync(Input(), key), HttpStatusCode.Conflict));
        Assert.Equal(ExpenseProblems.IdempotencyKeyReusedCode, await ProblemCodeAsync(await test.PostAsync(Input(grossAmount: "301.00"), key), HttpStatusCode.Conflict));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.RecurringExpenseDefinitions"));
        Assert.Equal(3, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.Expenses"));
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.RecurringDefinitionRequests"));
    }

    [Fact]
    public async Task Parallel_creates_with_the_same_key_create_one_definition()
    {
        await using var test = await CreateTestAsync(sqlServer, At(20, 11));
        var key = Guid.NewGuid();

        var responses = await Task.WhenAll(test.PostAsync(Input(), key), test.PostAsync(Input(), key));
        try
        {
            Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, r.StatusCode.ToString()));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.RecurringExpenseDefinitions"));
        Assert.Equal(3, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.Expenses"));
    }

    [Fact]
    public async Task Put_validates_fields_before_reading_then_version_then_paused_day_before_the_schedule()
    {
        await using var test = await CreateTestAsync(sqlServer, At(20, 11));
        var created = await test.CreateRecurringAsync(Input());
        const string Stale = "AAAAAAAAAAA=";

        Assert.Equal("grossAmount", await ProblemCodeAsync(await test.PutAsync(created.Id, Input(grossAmount: "abc"), Stale), HttpStatusCode.BadRequest));
        Assert.Equal(ExpenseProblems.RecurringConflictCode, await ProblemCodeAsync(await test.PutAsync(created.Id, Input(grossAmount: "350.00"), Stale), HttpStatusCode.Conflict));
        Assert.Equal(ExpenseProblems.RecurringNotFoundCode, await ProblemCodeAsync(await test.PutAsync(Guid.NewGuid(), Input(), Stale), HttpStatusCode.NotFound));

        await OkAsync(await test.ActionAsync(created.Id, "pause"));
        var pausedVersion = await test.VersionAsync(created.Id);

        Assert.Equal(
            ExpenseProblems.RecurringPausedCode,
            await ProblemCodeAsync(await test.PutAsync(created.Id, Input(dayOfMonth: 3, startsOn: "2026-03-15", endsOn: "2026-03-20")), HttpStatusCode.Conflict));
        Assert.Equal(pausedVersion, await test.VersionAsync(created.Id));
        Assert.Equal(
            ExpenseProblems.RecurringPausedCode,
            await ProblemCodeAsync(await test.SendAsync(HttpMethod.Get, $"{Base}/preview?dayOfMonth=3&startsOn=2026-09-01&definitionId={created.Id}"), HttpStatusCode.Conflict));

        var pausedPreview = await test.PreviewAsync($"dayOfMonth=10&startsOn=2026-09-01&definitionId={created.Id}");
        Assert.True(pausedPreview.GetProperty("paused").GetBoolean());
        Assert.Equal(0, pausedPreview.GetProperty("occurrencesToCreate").GetInt32());
        Assert.Equal(JsonValueKind.Null, pausedPreview.GetProperty("nextDueDate").ValueKind);

        var (createdWhilePaused, _) = await OkAsync(await test.PutAsync(created.Id, Input(grossAmount: "450.00", netAmount: null, endsOn: "2027-12-31")));
        Assert.Equal(0, createdWhilePaused);
        Assert.Equal(10, (await test.DetailsAsync(created.Id)).GetProperty("dayOfMonth").GetInt32());
    }

    [Fact]
    public async Task Returned_version_works_in_the_next_put_and_a_put_without_changes_keeps_it_without_audit()
    {
        await using var test = await CreateTestAsync(sqlServer, At(20, 11));
        var created = await test.CreateRecurringAsync(Input());

        var (_, changed) = await OkAsync(await test.PutAsync(created.Id, Input(grossAmount: "350.00", netAmount: null), created.Version));
        var (createdNow, unchanged) = await OkAsync(await test.PutAsync(created.Id, Input(grossAmount: "350.00", netAmount: null), changed));

        Assert.NotEqual(created.Version, changed);
        Assert.Equal(changed, unchanged);
        Assert.Equal(0, createdNow);
        var audits = await AuditAsync(test, "RecurringExpenseDefinition", created.Id);
        Assert.Equal(["Created", "Updated"], audits.Select(a => a.Operation));
        Assert.Contains("grossAmount", audits[1].Changes, StringComparison.Ordinal);
        Assert.Contains("netAmount", audits[1].Changes, StringComparison.Ordinal);
        Assert.DoesNotContain("dayOfMonth", audits[1].Changes, StringComparison.Ordinal);
        Assert.Equal(["300.00"], (await test.RowsAsync(
            $"SELECT CONVERT(varchar(20), GrossAmount) FROM expenses.Expenses WHERE RecurringDefinitionId = '{created.Id}' GROUP BY GrossAmount",
            r => r.GetString(0))).ToArray());
    }

    [Fact]
    public async Task Paused_definition_rejects_a_day_change_and_resume_leaves_the_pause_empty_then_a_day_change_creates_november()
    {
        await using var test = await CreateTestAsync(sqlServer, At(5, 11));
        var created = await test.CreateRecurringAsync(Input(startsOn: "2026-10-01"));
        var (pausedCreated, _) = await OkAsync(await test.ActionAsync(created.Id, "pause"));
        await test.AdvanceToAsync(At(20, 11));

        Assert.Equal(ExpenseProblems.RecurringPausedCode, await ProblemCodeAsync(await test.PutAsync(created.Id, Input(dayOfMonth: 3, startsOn: "2026-10-01")), HttpStatusCode.Conflict));
        var paused = await test.DetailsAsync(created.Id);
        var (resumedCreated, _) = await OkAsync(await test.ActionAsync(created.Id, "resume"));

        Assert.Equal(1, created.CreatedOccurrences);
        Assert.Equal(0, pausedCreated);
        Assert.Equal("paused", paused.GetProperty("status").GetString());
        Assert.Equal("2026-11-05", paused.GetProperty("pausedOn").GetString());
        Assert.Equal(JsonValueKind.Null, paused.GetProperty("nextDueDate").ValueKind);
        Assert.Equal(0, paused.GetProperty("resumePreview").GetProperty("occurrencesToCreate").GetInt32());
        Assert.Equal("2026-12-10", paused.GetProperty("resumePreview").GetProperty("nextDueDate").GetString());
        Assert.Equal(0, resumedCreated);
        Assert.Equal(["2026-10-10"], await test.OccurrencesAsync(created.Id));
        Assert.Equal("2026-12-10", (await test.DetailsAsync(created.Id)).GetProperty("nextDueDate").GetString());
        Assert.Equal(
            [("2026-11-05", "2026-11-20", "Pause")],
            await test.RowsAsync(
                $"SELECT CONVERT(char(10), FromDate, 23), CONVERT(char(10), ToDate, 23), Reason FROM expenses.RecurringExpenseExclusions WHERE DefinitionId = '{created.Id}'",
                r => (r.GetString(0), r.GetString(1), r.GetString(2))));

        var (dayCreated, _) = await OkAsync(await test.PutAsync(created.Id, Input(dayOfMonth: 3, startsOn: "2026-10-01")));

        Assert.Equal(1, dayCreated);
        Assert.Equal(["2026-10-10", "2026-11-03"], await test.OccurrencesAsync(created.Id));
    }

    [Fact]
    public async Task Pause_creates_the_due_occurrence_of_today_and_actions_report_their_rules_after_the_version()
    {
        await using var test = await CreateTestAsync(sqlServer, At(19, 11));
        var created = await test.CreateRecurringAsync(Input(dayOfMonth: 20, startsOn: "2026-10-01"));
        var ended = await test.CreateRecurringAsync(Input(dayOfMonth: 28, startsOn: "2026-10-01", endsOn: "2026-11-25"));
        await test.AdvanceToAsync(At(20, 11));

        var (pausedCreated, pausedVersion) = await OkAsync(await test.ActionAsync(created.Id, "pause"));

        Assert.Equal(1, pausedCreated);
        Assert.Equal(["2026-10-20", "2026-11-20"], await test.OccurrencesAsync(created.Id));
        Assert.Equal(ExpenseProblems.RecurringConflictCode, await ProblemCodeAsync(await test.ActionAsync(created.Id, "pause", created.Version), HttpStatusCode.Conflict));
        Assert.Equal(ExpenseProblems.RecurringAlreadyPausedCode, await ProblemCodeAsync(await test.ActionAsync(created.Id, "pause", pausedVersion), HttpStatusCode.Conflict));
        Assert.Equal(ExpenseProblems.RecurringEndedCode, await ProblemCodeAsync(await test.ActionAsync(ended.Id, "pause"), HttpStatusCode.Conflict));
        Assert.Equal(ExpenseProblems.RecurringNotPausedCode, await ProblemCodeAsync(await test.ActionAsync(ended.Id, "resume"), HttpStatusCode.Conflict));
        Assert.Equal(ExpenseProblems.RecurringNotFoundCode, await ProblemCodeAsync(await test.ActionAsync(Guid.NewGuid(), "pause", pausedVersion), HttpStatusCode.NotFound));
        Assert.Equal("version", await ProblemCodeAsync(await test.SendAsync(HttpMethod.Post, $"{Base}/{created.Id}/resume", new Dictionary<string, object?>()), HttpStatusCode.BadRequest));
        Assert.Equal(ExpenseProblems.RecurringNotFoundCode, await ProblemCodeAsync(await test.SendAsync(HttpMethod.Get, $"{Base}/{Guid.NewGuid()}"), HttpStatusCode.NotFound));

        var (resumedCreated, _) = await OkAsync(await test.ActionAsync(created.Id, "resume", pausedVersion));
        Assert.Equal(0, resumedCreated);
        var audits = await AuditAsync(test, "RecurringExpenseDefinition", created.Id);
        Assert.Equal(["Created", "Updated", "Updated"], audits.Select(a => a.Operation));
        Assert.Contains("\"pausedOn\"", audits[1].Changes, StringComparison.Ordinal);
        Assert.Contains("\"pausedOn\"", audits[2].Changes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Day_change_on_the_creation_day_replaces_the_entry_and_a_passed_new_due_date_is_created_as_previewed()
    {
        await using var test = await CreateTestAsync(sqlServer, At(20, 11));
        var sameDay = await test.CreateRecurringAsync(Input(startsOn: "2026-11-20"));
        var current = await test.CreateRecurringAsync(Input(dayOfMonth: 25, startsOn: "2026-10-01"));

        var (sameDayCreated, _) = await OkAsync(await test.PutAsync(sameDay.Id, Input(dayOfMonth: 25, startsOn: "2026-11-20")));
        var preview = await test.PreviewAsync($"dayOfMonth=5&startsOn=2026-10-01&definitionId={current.Id}");
        var (currentCreated, _) = await OkAsync(await test.PutAsync(current.Id, Input(dayOfMonth: 5, startsOn: "2026-10-01")));

        Assert.Equal(0, sameDayCreated);
        Assert.Equal("2026-11-25", (await test.DetailsAsync(sameDay.Id)).GetProperty("nextDueDate").GetString());
        Assert.Equal(
            [("2026-11-01", 25)],
            await test.RowsAsync($"SELECT CONVERT(char(10), FromPeriod, 23), CAST(DayOfMonth AS int) FROM expenses.RecurringExpenseDays WHERE DefinitionId = '{sameDay.Id}'", r => (r.GetString(0), r.GetInt32(1))));
        Assert.Equal(1, preview.GetProperty("occurrencesToCreate").GetInt32());
        Assert.Equal("2026-11-05", preview.GetProperty("firstImmediateDueDate").GetString());
        Assert.Equal(1, currentCreated);
        Assert.Equal(["2026-10-25", "2026-11-05"], await test.OccurrencesAsync(current.Id));
    }

    [Fact]
    public async Task Day_then_extension_creates_june_to_october_with_the_old_day_and_november_with_the_new_one()
    {
        await using var test = await CreateTestAsync(sqlServer, At(20, 11));
        var created = await test.CreateRecurringAsync(Input(dayOfMonth: 28, startsOn: "2026-01-01", endsOn: "2026-06-25"));

        var (dayCreated, _) = await OkAsync(await test.PutAsync(created.Id, Input(dayOfMonth: 20, startsOn: "2026-01-01", endsOn: "2026-06-25")));
        var endedDetails = await test.DetailsAsync(created.Id);
        await test.AdvanceToAsync(At(21, 11));
        var preview = await test.PreviewAsync($"dayOfMonth=20&startsOn=2026-01-01&definitionId={created.Id}");
        var (extensionCreated, _) = await OkAsync(await test.PutAsync(created.Id, Input(dayOfMonth: 20, startsOn: "2026-01-01")));

        Assert.Equal(5, created.CreatedOccurrences);
        Assert.Equal(0, dayCreated);
        Assert.Equal("ended", endedDetails.GetProperty("status").GetString());
        Assert.Equal(6, preview.GetProperty("occurrencesToCreate").GetInt32());
        Assert.Equal(6, extensionCreated);
        Assert.Equal(
            ["2026-01-28", "2026-02-28", "2026-03-28", "2026-04-28", "2026-05-28", "2026-06-28", "2026-07-28", "2026-08-28", "2026-09-28", "2026-10-28", "2026-11-20"],
            await test.OccurrencesAsync(created.Id));
    }

    [Fact]
    public async Task Due_uncreated_period_dropping_out_is_previewed_and_an_earlier_start_counts_today_but_not_yesterday()
    {
        await using var test = await CreateTestAsync(sqlServer, At(5, 11));
        var dropping = await test.CreateRecurringAsync(Input(startsOn: "2026-11-05"));
        await test.AdvanceToAsync(At(20, 11));
        var today = await test.CreateRecurringAsync(Input(dayOfMonth: 20, startsOn: "2026-12-01"));
        var yesterday = await test.CreateRecurringAsync(Input(dayOfMonth: 19, startsOn: "2026-12-01"));

        var dropPreview = await test.PreviewAsync($"dayOfMonth=3&startsOn=2026-11-05&definitionId={dropping.Id}");
        var (dropCreated, _) = await OkAsync(await test.PutAsync(dropping.Id, Input(dayOfMonth: 3, startsOn: "2026-11-05")));
        var todayPreview = await test.PreviewAsync($"dayOfMonth=20&startsOn=2026-11-01&definitionId={today.Id}");
        var (todayCreated, _) = await OkAsync(await test.PutAsync(today.Id, Input(dayOfMonth: 20, startsOn: "2026-11-01")));
        var (yesterdayCreated, _) = await OkAsync(await test.PutAsync(yesterday.Id, Input(dayOfMonth: 19, startsOn: "2026-11-01")));

        Assert.Equal(1, dropPreview.GetProperty("droppedDueCount").GetInt32());
        Assert.Equal(0, dropPreview.GetProperty("occurrencesToCreate").GetInt32());
        Assert.Equal("2026-12-03", dropPreview.GetProperty("nextDueDate").GetString());
        Assert.Equal(0, dropCreated);
        Assert.Equal(1, todayPreview.GetProperty("occurrencesToCreate").GetInt32());
        Assert.Equal(1, todayCreated);
        Assert.Equal(["2026-11-20"], await test.OccurrencesAsync(today.Id));
        Assert.Equal(0, yesterdayCreated);
        Assert.Equal("2026-12-19", (await test.DetailsAsync(yesterday.Id)).GetProperty("nextDueDate").GetString());
        Assert.Equal(
            [("2026-11-01", "2026-11-20", "StartMoved")],
            await test.RowsAsync(
                $"SELECT CONVERT(char(10), FromDate, 23), CONVERT(char(10), ToDate, 23), Reason FROM expenses.RecurringExpenseExclusions WHERE DefinitionId = '{today.Id}'",
                r => (r.GetString(0), r.GetString(1), r.GetString(2))));
    }

    [Fact]
    public async Task Shortening_then_extending_during_a_pause_creates_nothing_from_the_pause_after_resume()
    {
        await using var test = await CreateTestAsync(sqlServer, At(10, 7));
        var created = await test.CreateRecurringAsync(Input(startsOn: "2026-01-01", endsOn: "2026-12-31"));
        await test.AdvanceToAsync(At(5, 8));
        await OkAsync(await test.ActionAsync(created.Id, "pause"));
        await test.AdvanceToAsync(At(1, 9));
        var (shortened, _) = await OkAsync(await test.PutAsync(created.Id, Input(startsOn: "2026-01-01", endsOn: "2026-07-31")));
        var (extended, _) = await OkAsync(await test.PutAsync(created.Id, Input(startsOn: "2026-01-01")));
        await test.AdvanceToAsync(At(20, 11));

        var (resumed, _) = await OkAsync(await test.ActionAsync(created.Id, "resume"));

        Assert.Equal(7, created.CreatedOccurrences);
        Assert.Equal((0, 0, 0), (shortened, extended, resumed));
        Assert.Equal(7, (await test.OccurrencesAsync(created.Id)).Count);
        Assert.Equal("2026-12-10", (await test.DetailsAsync(created.Id)).GetProperty("nextDueDate").GetString());
    }

    [Fact]
    public async Task Delete_cascades_schedule_and_registry_but_keeps_occurrences_the_request_and_a_trace()
    {
        await using var test = await CreateTestAsync(sqlServer, At(20, 11));
        var created = await test.CreateRecurringAsync(Input());
        await OkAsync(await test.ActionAsync(created.Id, "pause"));
        await test.AdvanceToAsync(At(22, 11));
        var (_, version) = await OkAsync(await test.ActionAsync(created.Id, "resume"));

        Assert.Equal(
            ExpenseProblems.RecurringConflictCode,
            await ProblemCodeAsync(await test.SendAsync(HttpMethod.Delete, $"{Base}/{created.Id}", new Dictionary<string, object?> { ["version"] = created.Version }), HttpStatusCode.Conflict));
        using var deleted = await test.SendAsync(HttpMethod.Delete, $"{Base}/{created.Id}", new Dictionary<string, object?> { ["version"] = version });

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        foreach (var table in new[] { "RecurringExpenseDefinitions", "RecurringExpenseDays", "RecurringExpenseExclusions", "RecurringExpensePeriods" })
        {
            Assert.Equal(0, await test.ScalarAsync<int>($"SELECT COUNT(*) FROM expenses.{table}"));
        }

        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.RecurringDefinitionRequests"));
        Assert.Equal(3, await test.ScalarAsync<int>($"SELECT COUNT(*) FROM expenses.Expenses WHERE RecurringDefinitionId = '{created.Id}'"));
        Assert.Equal("Deleted", (await AuditAsync(test, "RecurringExpenseDefinition", created.Id))[^1].Operation);
        Assert.Equal(ExpenseProblems.RecurringNotFoundCode, await ProblemCodeAsync(await test.SendAsync(HttpMethod.Get, $"{Base}/{created.Id}"), HttpStatusCode.NotFound));
        var occurrence = (await test.GetJsonAsync($"/api/expenses?recurringDefinitionId={created.Id}")).GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.False((await test.GetJsonAsync($"/api/expenses/{occurrence}")).GetProperty("recurring").GetProperty("definitionExists").GetBoolean());
    }

    [Fact]
    public async Task Changes_of_an_occurrence_are_audited_with_the_definition_as_parent_and_a_deleted_one_is_not_recreated()
    {
        await using var test = await CreateTestAsync(sqlServer, At(20, 11));
        var created = await test.CreateRecurringAsync(Input());
        var occurrences = (await test.GetJsonAsync($"/api/expenses?recurringDefinitionId={created.Id}&sort=costDate&direction=asc")).GetProperty("items")
            .EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        var paid = await test.GetJsonAsync($"/api/expenses/{occurrences[0]}");
        var removed = await test.GetJsonAsync($"/api/expenses/{occurrences[1]}");

        await test.ChangeAsync(
            HttpMethod.Post,
            $"/api/expenses/{occurrences[0]}/mark-paid",
            new Dictionary<string, object?> { ["paidOn"] = "2026-11-20", ["paymentMethod"] = "blik", ["version"] = paid.GetProperty("version").GetString() });
        using var delete = await test.SendAsync(
            HttpMethod.Delete, $"/api/expenses/{occurrences[1]}", new Dictionary<string, object?> { ["version"] = removed.GetProperty("version").GetString() });
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        var jobStatus = await test.RunJobAsync();
        var (extensionCreated, _) = await OkAsync(await test.PutAsync(created.Id, Input(endsOn: "2027-12-31")));

        var audits = await test.RowsAsync(
            $"SELECT Operation, AuthorKind, ParentObjectType, ParentObjectId FROM reporting.AuditEntries WHERE ObjectType = N'Expense' AND Operation <> N'Created' ORDER BY Id",
            r => (r.GetString(0), r.GetString(1), r.GetString(2), r.GetGuid(3)));
        Assert.Equal([("Updated", "Account", "RecurringExpenseDefinition", created.Id), ("Deleted", "Account", "RecurringExpenseDefinition", created.Id)], audits);
        Assert.Equal(StoneFlow.Shared.Application.Scheduling.JobRunStatus.Succeeded, jobStatus);
        Assert.Equal(0, extensionCreated);
        Assert.Equal(["2026-09-10", "2026-11-10"], await test.OccurrencesAsync(created.Id));
    }

    [Fact]
    public async Task Registry_collision_during_an_edit_returns_503_busy_and_rolls_everything_back()
    {
        var hooks = new RecurringStoreHooks();
        await using var test = await CreateTestAsync(sqlServer, At(20, 11), hooks);
        var created = await test.CreateRecurringAsync(Input(dayOfMonth: 25, startsOn: "2026-10-01"));
        hooks.BeforeAddOccurrences = (_, context, batch) => RecurringRaces.InsertConflictingPeriodAsync(context, batch[0]);

        Assert.Equal(ExpenseProblems.RecurringBusyCode, await ProblemCodeAsync(await test.PutAsync(created.Id, Input(dayOfMonth: 5, startsOn: "2026-10-01")), HttpStatusCode.ServiceUnavailable));

        var details = await test.DetailsAsync(created.Id);
        Assert.Equal(created.Version, details.GetProperty("version").GetString());
        Assert.Equal(25, details.GetProperty("dayOfMonth").GetInt32());
        Assert.Equal(["2026-10-25"], await test.OccurrencesAsync(created.Id));
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.RecurringExpensePeriods"));
        Assert.Single(await AuditAsync(test, "RecurringExpenseDefinition", created.Id));
    }

    [Fact]
    public async Task Deadlock_during_an_edit_goes_to_the_503_fallback_without_saving()
    {
        var hooks = new RecurringStoreHooks();
        await using var test = await CreateTestAsync(sqlServer, At(20, 11), hooks);
        var created = await test.CreateRecurringAsync(Input(dayOfMonth: 25, startsOn: "2026-10-01"));
        var deadlock = await RecurringRaces.DeadlockAsync(test.ConnectionString);
        hooks.BeforeAddOccurrences = (_, _, _) => throw deadlock;

        using var response = await test.PutAsync(created.Id, Input(dayOfMonth: 5, startsOn: "2026-10-01"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(created.Version, await test.VersionAsync(created.Id));
        Assert.Equal(["2026-10-25"], await test.OccurrencesAsync(created.Id));
        Assert.Single(await AuditAsync(test, "RecurringExpenseDefinition", created.Id));
    }

    [Fact]
    public async Task List_state_filter_agrees_with_the_status_and_supports_search_and_sorting()
    {
        await using var test = await CreateTestAsync(sqlServer, At(20, 11));
        var active = await test.CreateRecurringAsync(Input(description: "Abonament A", grossAmount: "100.00", netAmount: null));
        var paused = await test.CreateRecurringAsync(Input(description: "Abonament B", grossAmount: "200.00", netAmount: null));
        var ended = await test.CreateRecurringAsync(Input(description: "Czynsz", dayOfMonth: 28, endsOn: "2026-11-25", grossAmount: "300.00", netAmount: null));
        await OkAsync(await test.ActionAsync(paused.Id, "pause"));

        foreach (var (state, id) in new[] { ("active", active.Id), ("paused", paused.Id), ("ended", ended.Id) })
        {
            var item = Assert.Single((await test.GetJsonAsync($"{Base}?state={state}")).GetProperty("items").EnumerateArray());
            Assert.Equal(id, item.GetProperty("id").GetGuid());
            Assert.Equal(state, item.GetProperty("status").GetString());
        }

        var all = await test.GetJsonAsync($"{Base}?sort=grossAmount&direction=desc");
        Assert.Equal(3, all.GetProperty("total").GetInt32());
        Assert.Equal([ended.Id, paused.Id, active.Id], all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
        var byDescription = await test.GetJsonAsync(Base);
        Assert.Equal([active.Id, paused.Id, ended.Id], byDescription.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
        var first = byDescription.GetProperty("items")[0];
        Assert.Equal("2026-12-10", first.GetProperty("nextDueDate").GetString());
        Assert.Equal(10, first.GetProperty("dayOfMonth").GetInt32());
        Assert.Equal(JsonValueKind.Null, byDescription.GetProperty("items")[1].GetProperty("nextDueDate").ValueKind);
        Assert.Equal(2, (await test.GetJsonAsync($"{Base}?search=bonament")).GetProperty("total").GetInt32());
        Assert.Equal("pageSize", await ProblemCodeAsync(await test.SendAsync(HttpMethod.Get, $"{Base}?pageSize=101"), HttpStatusCode.BadRequest));
        Assert.Equal("state", await ProblemCodeAsync(await test.SendAsync(HttpMethod.Get, $"{Base}?state=planned"), HttpStatusCode.BadRequest));
        Assert.Equal("recurringDefinitionId", await ProblemCodeAsync(await test.SendAsync(HttpMethod.Get, "/api/expenses?recurringDefinitionId=abc"), HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Database_constraints_reject_inconsistent_rows_and_a_second_registry_row_for_a_period()
    {
        await using var test = await CreateTestAsync(sqlServer, At(20, 11));
        var created = await test.CreateRecurringAsync(Input());
        var id = created.Id;
        const string Columns = "INSERT INTO expenses.RecurringExpenseDefinitions (Id, Description, Category, GrossAmount, DayOfMonth, StartsOn, EndsOn, LastDueDate, CreatedAtUtc, UpdatedAtUtc) VALUES (NEWID(), N'X', N'Marketing', ";

        string[] violations =
        [
            Columns + "0, 10, '2026-01-01', NULL, NULL, SYSUTCDATETIME(), SYSUTCDATETIME())",
            Columns + "10, 32, '2026-01-01', NULL, NULL, SYSUTCDATETIME(), SYSUTCDATETIME())",
            Columns + "10, 10, '2026-02-01', '2026-01-01', '2026-01-10', SYSUTCDATETIME(), SYSUTCDATETIME())",
            Columns + "10, 10, '2026-01-01', '2026-12-31', NULL, SYSUTCDATETIME(), SYSUTCDATETIME())",
            Columns + "10, 10, '2026-01-01', NULL, '2026-01-10', SYSUTCDATETIME(), SYSUTCDATETIME())",
            Columns + "10, 10, '2026-01-01', '2026-03-31', '2026-04-10', SYSUTCDATETIME(), SYSUTCDATETIME())",
            $"INSERT INTO expenses.RecurringExpenseDays (DefinitionId, FromPeriod, DayOfMonth) VALUES ('{id}', '2027-01-02', 10)",
            $"INSERT INTO expenses.RecurringExpenseExclusions (Id, DefinitionId, FromDate, ToDate, Reason, CreatedAtUtc) VALUES (NEWID(), '{id}', '2026-01-10', '2026-01-10', N'Pause', SYSUTCDATETIME())",
            $"INSERT INTO expenses.RecurringExpenseExclusions (Id, DefinitionId, FromDate, ToDate, Reason, CreatedAtUtc) VALUES (NEWID(), '{id}', '2026-01-10', '2026-01-11', N'Other', SYSUTCDATETIME())",
            $"INSERT INTO expenses.RecurringExpensePeriods (DefinitionId, Period, DueDate, ExpenseId, CreatedAtUtc) VALUES ('{id}', '2027-01-02', '2027-01-02', NEWID(), SYSUTCDATETIME())",
            "UPDATE TOP (1) expenses.Expenses SET RecurringPeriod = NULL",
            $"UPDATE expenses.RecurringExpenseDefinitions SET NetAmount = GrossAmount + 1 WHERE Id = '{id}'",
            $"UPDATE expenses.RecurringExpenseDefinitions SET NetAmount = -1 WHERE Id = '{id}'",
            $"INSERT INTO expenses.RecurringExpenseDays (DefinitionId, FromPeriod, DayOfMonth) VALUES ('{id}', '2027-01-01', 0)",
            $"INSERT INTO expenses.RecurringDefinitionRequests ([Key], DefinitionId, RequestHash, CreatedOccurrences, CreatedAtUtc) VALUES (NEWID(), '{id}', CONVERT(binary(32), 0x01), -1, SYSUTCDATETIME())",
        ];

        foreach (var sql in violations)
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => test.ExecuteSqlAsync(sql));
            Assert.True(error.Number == 547, $"{error.Number}: {sql}");
        }

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => test.ExecuteSqlAsync(
            $"INSERT INTO expenses.RecurringExpensePeriods (DefinitionId, Period, DueDate, ExpenseId, CreatedAtUtc) VALUES ('{id}', '2026-09-01', '2026-09-10', NEWID(), SYSUTCDATETIME())"));
        Assert.Equal(2627, duplicate.Number);
    }

    [Fact]
    public async Task Audit_failure_of_an_occurrence_rolls_back_the_definition_together_with_its_occurrences()
    {
        await using var test = await CreateTestAsync(sqlServer, At(20, 11));

        // Odrzuca tylko wpisy audytu wydatków: audyt definicji przechodzi, audyt wystąpienia nie.
        await test.ExecuteSqlAsync("ALTER TABLE reporting.AuditEntries WITH NOCHECK ADD CONSTRAINT CK_Test_RejectExpenses CHECK (ObjectType <> N'Expense');");

        using var response = await test.PostAsync(Input(dayOfMonth: 10, startsOn: "2026-08-01"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.RecurringExpenseDefinitions"));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.Expenses"));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.RecurringExpensePeriods"));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.RecurringDefinitionRequests"));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM reporting.AuditEntries WHERE ObjectType = N'RecurringExpenseDefinition'"));
    }

    [Fact]
    public async Task Definition_without_day_history_seen_mid_deletion_is_skipped_by_list_details_and_job()
    {
        await using var test = await CreateTestAsync(sqlServer, At(20, 11));
        var broken = await test.CreateRecurringAsync(Input(dayOfMonth: 25, startsOn: "2026-11-01"));
        var kept = await test.CreateRecurringAsync(Input(dayOfMonth: 25, startsOn: "2026-11-01"));

        // Stan widziany przez odczyt w trakcie usuwania definicji: nagłówek jest, historii dni już nie.
        await test.ExecuteSqlAsync($"DELETE FROM expenses.RecurringExpenseDays WHERE DefinitionId = '{broken.Id}'");

        using var details = await test.SendAsync(HttpMethod.Get, $"{Base}/{broken.Id}");
        Assert.Equal(HttpStatusCode.NotFound, details.StatusCode);
        var items = (await test.GetJsonAsync(Base)).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        Assert.Equal([kept.Id], items);

        test.Time.SetUtcNow(At(25, 11));
        Assert.Equal(StoneFlow.Shared.Application.Scheduling.JobRunStatus.Succeeded, await test.RunJobAsync());
        Assert.Equal(["2026-11-25"], await test.OccurrencesAsync(kept.Id));
    }

    private static async Task<IReadOnlyList<AuditRow>> AuditAsync(ExpensesTest test, string type, Guid id) =>
        await test.RowsAsync(
            $"SELECT Operation, AuthorKind, ParentObjectType, Changes FROM reporting.AuditEntries WHERE ObjectType = N'{type}' AND ObjectId = '{id}' ORDER BY Id",
            r => new AuditRow(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3)));

    private sealed record AuditRow(string Operation, string AuthorKind, string? Parent, string Changes);
}
