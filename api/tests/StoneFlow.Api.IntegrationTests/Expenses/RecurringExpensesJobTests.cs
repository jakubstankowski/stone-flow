using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Expenses.Application.GenerateRecurringOccurrences;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.Scheduling;
using static StoneFlow.Api.IntegrationTests.Expenses.Recurring;

namespace StoneFlow.Api.IntegrationTests.Expenses;

/// <summary>
/// SF-16: zadanie <c>recurring-expenses</c> przez runner harmonogramu na prawdziwej bazie — termin
/// dzisiaj, powtórne przebiegi, nadrabianie przerwy, odcięcie 400 dni, błąd jednej definicji,
/// anulowanie, ponowienie po kolizji i zakleszczeniu, współbieżność i wydajność. Dane fikcyjne.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class RecurringExpensesJobTests(SqlServerFixture sqlServer)
{
    [Fact]
    public async Task Run_creates_the_occurrence_on_its_due_day_in_warsaw_and_a_second_run_creates_nothing()
    {
        await using var test = await CreateTestAsync(sqlServer, At(19, 11));
        var created = await test.CreateRecurringAsync(Input(dayOfMonth: 20, startsOn: "2026-10-01"));

        // 23:15 UTC 19.11 to już 00:15 20.11 w Warszawie (zima, UTC+1).
        test.Time.SetUtcNow(new DateTimeOffset(2026, 11, 19, 23, 15, 0, TimeSpan.Zero));
        var first = await test.RunJobAsync();
        var second = await test.RunJobAsync();

        Assert.Equal((JobRunStatus.Succeeded, JobRunStatus.Succeeded), (first, second));
        Assert.Equal(["2026-10-20", "2026-11-20"], await test.OccurrencesAsync(created.Id));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM reporting.AuditEntries WHERE ObjectType = N'Expense' AND AuthorKind = N'AutomatedJob' AND ParentObjectType = N'RecurringExpenseDefinition' AND ObjectBusinessDate = '2026-11-20'"));
    }

    [Fact]
    public async Task First_run_after_downtime_creates_every_missed_period_with_its_own_due_date()
    {
        await using var test = await CreateTestAsync(sqlServer, At(10, 11));
        var created = await test.CreateRecurringAsync(Input(startsOn: "2026-11-01"));

        test.Time.SetUtcNow(At(20, 2, 2027));
        var status = await test.RunJobAsync();

        Assert.Equal(JobRunStatus.Succeeded, status);
        Assert.Equal(["2026-11-10", "2026-12-10", "2027-01-10", "2027-02-10"], await test.OccurrencesAsync(created.Id));
    }

    [Fact]
    public async Task Definitions_ended_more_than_400_days_ago_are_not_candidates()
    {
        await using var test = await CreateTestAsync(sqlServer, At(20, 11));
        var old = await test.CreateRecurringAsync(Input(startsOn: "2024-01-01", endsOn: "2025-01-31"));
        var recent = await test.CreateRecurringAsync(Input(startsOn: "2025-01-01", endsOn: "2025-12-31"));
        await ForgetPeriodAsync(test, old.Id, "2025-01-01");
        await ForgetPeriodAsync(test, recent.Id, "2025-12-01");

        var status = await test.RunJobAsync();

        Assert.Equal(JobRunStatus.Succeeded, status);
        Assert.Equal(12, (await test.OccurrencesAsync(old.Id)).Count);
        Assert.Equal(12, (await test.OccurrencesAsync(recent.Id)).Count);
        Assert.Contains("2025-12-10", await test.OccurrencesAsync(recent.Id));
    }

    [Fact]
    public async Task Failure_of_one_definition_does_not_block_others_and_fails_the_run_without_personal_data()
    {
        var hooks = new GenerateHooks();
        await using var test = await CreateTestAsync(sqlServer, At(19, 11), configureServices: services => GenerateHooks.Register(services, hooks));
        var failing = await test.CreateRecurringAsync(Input(dayOfMonth: 20, startsOn: "2026-10-01", payer: "Jan Testowy"));
        var working = await test.CreateRecurringAsync(Input(dayOfMonth: 20, startsOn: "2026-10-01"));
        hooks.Before = command => command.DefinitionId == failing.Id ? throw new InvalidOperationException("Testowy błąd.") : Task.CompletedTask;
        test.Time.SetUtcNow(At(20, 11));

        var failed = await test.RunJobAsync();

        Assert.Equal(JobRunStatus.Failed, failed);
        Assert.Equal(["2026-10-20"], await test.OccurrencesAsync(failing.Id));
        Assert.Equal(["2026-10-20", "2026-11-20"], await test.OccurrencesAsync(working.Id));
        var error = Assert.Single(await test.LastJobErrorAsync());
        Assert.Contains("Nie utworzono wystąpień dla 1 wydatków cyklicznych.", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Abonament", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Testowy", error, StringComparison.Ordinal);

        hooks.Before = null;
        Assert.Equal(JobRunStatus.Succeeded, await test.RunJobAsync());
        Assert.Equal(["2026-10-20", "2026-11-20"], await test.OccurrencesAsync(failing.Id));
    }

    [Fact]
    public async Task Cancelled_run_stops_after_the_current_definition_and_the_next_run_creates_only_the_missing_ones()
    {
        var hooks = new GenerateHooks();
        await using var test = await CreateTestAsync(sqlServer, At(19, 11), configureServices: services => GenerateHooks.Register(services, hooks));
        var first = await test.CreateRecurringAsync(Input(dayOfMonth: 20, startsOn: "2026-10-01"));
        var second = await test.CreateRecurringAsync(Input(dayOfMonth: 20, startsOn: "2026-10-01"));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        hooks.After = () => cancellation.CancelAsync();
        test.Time.SetUtcNow(At(20, 11));

        try
        {
            await test.RunJobAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Anulowanie może przejść do wywołującego runnera.
        }

        var afterCancel = (await test.OccurrencesAsync(first.Id)).Count + (await test.OccurrencesAsync(second.Id)).Count;
        hooks.After = null;
        var rerun = await test.RunJobAsync();

        Assert.Equal(3, afterCancel);
        Assert.Equal(JobRunStatus.Succeeded, rerun);
        Assert.Equal(["2026-10-20", "2026-11-20"], await test.OccurrencesAsync(first.Id));
        Assert.Equal(["2026-10-20", "2026-11-20"], await test.OccurrencesAsync(second.Id));
    }

    [Fact]
    public async Task Registry_collision_is_retried_once_in_a_new_scope()
    {
        var hooks = new RecurringStoreHooks();
        await using var test = await CreateTestAsync(sqlServer, At(19, 11), hooks);
        var created = await test.CreateRecurringAsync(Input(dayOfMonth: 20, startsOn: "2026-10-01"));
        var callsBefore = hooks.Calls;
        hooks.BeforeAddOccurrences = (call, context, batch) =>
            call == callsBefore + 1 ? RecurringRaces.InsertConflictingPeriodAsync(context, batch[0]) : Task.CompletedTask;
        test.Time.SetUtcNow(At(20, 11));

        var status = await test.RunJobAsync();

        Assert.Equal(JobRunStatus.Succeeded, status);
        Assert.Equal(callsBefore + 2, hooks.Calls);
        Assert.Equal(["2026-10-20", "2026-11-20"], await test.OccurrencesAsync(created.Id));
        Assert.Equal(2, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.RecurringExpensePeriods"));
    }

    [Fact]
    public async Task Deadlock_is_retried_once_in_a_new_scope()
    {
        var hooks = new RecurringStoreHooks();
        await using var test = await CreateTestAsync(sqlServer, At(19, 11), hooks);
        var created = await test.CreateRecurringAsync(Input(dayOfMonth: 20, startsOn: "2026-10-01"));
        var deadlock = await RecurringRaces.DeadlockAsync(test.ConnectionString);
        var callsBefore = hooks.Calls;
        hooks.BeforeAddOccurrences = (call, _, _) => call == callsBefore + 1 ? throw deadlock : Task.CompletedTask;
        test.Time.SetUtcNow(At(20, 11));

        var status = await test.RunJobAsync();

        Assert.Equal(JobRunStatus.Succeeded, status);
        Assert.Equal(["2026-10-20", "2026-11-20"], await test.OccurrencesAsync(created.Id));
    }

    [Fact]
    public async Task Second_deadlock_counts_as_a_failure_of_the_definition()
    {
        var hooks = new RecurringStoreHooks();
        await using var test = await CreateTestAsync(sqlServer, At(19, 11), hooks);
        var created = await test.CreateRecurringAsync(Input(dayOfMonth: 20, startsOn: "2026-10-01"));
        var deadlock = await RecurringRaces.DeadlockAsync(test.ConnectionString);
        hooks.BeforeAddOccurrences = (_, _, _) => throw deadlock;
        test.Time.SetUtcNow(At(20, 11));

        var status = await test.RunJobAsync();

        Assert.Equal(JobRunStatus.Failed, status);
        Assert.Equal(["2026-10-20"], await test.OccurrencesAsync(created.Id));
    }

    [Fact]
    public async Task Definition_deleted_during_generation_is_not_an_error()
    {
        var hooks = new RecurringStoreHooks();
        await using var test = await CreateTestAsync(sqlServer, At(19, 11), hooks);
        var created = await test.CreateRecurringAsync(Input(dayOfMonth: 20, startsOn: "2026-10-01"));
        hooks.BeforeAddOccurrences = (_, context, _) =>
            context.Database.ExecuteSqlAsync($"DELETE FROM [expenses].[RecurringExpenseDefinitions] WHERE [Id] = {created.Id}");
        test.Time.SetUtcNow(At(20, 11));

        var status = await test.RunJobAsync();

        Assert.Equal(JobRunStatus.Succeeded, status);
        Assert.Equal(["2026-10-20"], await test.OccurrencesAsync(created.Id));
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.RecurringExpenseDefinitions"));
    }

    [Fact]
    public async Task Check_violation_while_inserting_occurrences_fails_the_run_instead_of_looking_like_a_missing_definition()
    {
        var hooks = new RecurringStoreHooks();
        await using var test = await CreateTestAsync(sqlServer, At(19, 11), hooks);
        var created = await test.CreateRecurringAsync(Input(dayOfMonth: 20, startsOn: "2026-10-01"));

        // Testowe ograniczenie CHECK w tej samej transakcji: wstawienie wystąpienia daje 547 dla CHECK,
        // a nie dla klucza obcego definicji. Wycofanie jednostki usuwa też ograniczenie.
        hooks.BeforeAddOccurrences = (_, context, _) => context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE [expenses].[RecurringExpensePeriods] WITH NOCHECK ADD CONSTRAINT [CK_Test_RejectPeriods] CHECK ([DueDate] < '2000-01-01')");
        test.Time.SetUtcNow(At(20, 11));

        var status = await test.RunJobAsync();

        Assert.Equal(JobRunStatus.Failed, status);
        Assert.Equal(["2026-10-20"], await test.OccurrencesAsync(created.Id));
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.RecurringExpenseDefinitions"));
    }

    [Fact]
    public async Task Parallel_units_of_work_for_one_definition_create_one_set()
    {
        await using var test = await CreateTestAsync(sqlServer, At(19, 11));
        var created = await test.CreateRecurringAsync(Input(dayOfMonth: 20, startsOn: "2026-09-01"));
        await test.ExecuteSqlAsync($"DELETE FROM expenses.RecurringExpensePeriods WHERE DefinitionId = '{created.Id}' AND Period = '2026-10-01'");
        test.Time.SetUtcNow(At(20, 11));

        var results = await Task.WhenAll(GenerateAsync(test, created.Id), GenerateAsync(test, created.Id));

        Assert.All(results, r => Assert.Equal(GenerateRecurringOccurrencesOutcome.Created, r.Outcome));
        Assert.Equal(2, results.Sum(r => r.Created));
        Assert.Equal(3, await test.ScalarAsync<int>($"SELECT COUNT(*) FROM expenses.RecurringExpensePeriods WHERE DefinitionId = '{created.Id}'"));
    }

    [Fact]
    public async Task Run_over_500_definitions_with_24_months_of_history_creates_exactly_the_100_missing_occurrences_within_60_seconds()
    {
        await using var test = await CreateTestAsync(sqlServer, At(20, 11));
        await test.ExecuteSqlAsync(
            """
            ;WITH N AS (SELECT TOP (500) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS I FROM sys.all_objects)
            INSERT expenses.RecurringExpenseDefinitions (Id, Description, Category, GrossAmount, DayOfMonth, StartsOn, CreatedAtUtc, UpdatedAtUtc)
            SELECT NEWID(), CONCAT(N'Abonament ', I), N'Marketing', 100, 10, '2024-12-01', SYSUTCDATETIME(), SYSUTCDATETIME() FROM N;
            INSERT expenses.RecurringExpenseDays (DefinitionId, FromPeriod, DayOfMonth)
            SELECT Id, '2024-12-01', 10 FROM expenses.RecurringExpenseDefinitions;
            ;WITH M AS (SELECT TOP (24) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS M FROM sys.all_objects),
                D AS (SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) AS R FROM expenses.RecurringExpenseDefinitions)
            INSERT expenses.RecurringExpensePeriods (DefinitionId, Period, DueDate, ExpenseId, CreatedAtUtc)
            SELECT D.Id, DATEADD(month, M.M, '2024-12-01'), DATEADD(day, 9, DATEADD(month, M.M, '2024-12-01')), NEWID(), SYSUTCDATETIME()
            FROM D CROSS JOIN M WHERE NOT (D.R <= 100 AND M.M = 23);
            """);
        var stopwatch = Stopwatch.StartNew();

        var status = await test.RunJobAsync();

        stopwatch.Stop();
        Assert.Equal(JobRunStatus.Succeeded, status);
        Assert.Equal(100, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.Expenses WHERE RecurringPeriod = '2026-11-01'"));
        Assert.Equal(500 * 24, await test.ScalarAsync<int>("SELECT COUNT(*) FROM expenses.RecurringExpensePeriods"));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60), $"Przebieg trwał {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task Create_from_the_year_2000_creates_every_occurrence_in_one_save_within_30_seconds()
    {
        await using var test = await CreateTestAsync(sqlServer, At(20, 11));
        var stopwatch = Stopwatch.StartNew();

        var created = await test.CreateRecurringAsync(Input(dayOfMonth: 15, startsOn: "2000-01-01"));

        stopwatch.Stop();
        Assert.Equal(323, created.CreatedOccurrences);
        Assert.Equal(323, await test.ScalarAsync<int>($"SELECT COUNT(*) FROM expenses.Expenses WHERE RecurringDefinitionId = '{created.Id}'"));
        Assert.Equal(323, await test.ScalarAsync<int>("SELECT COUNT(*) FROM reporting.AuditEntries WHERE ObjectType = N'Expense'"));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"Zapis trwał {stopwatch.Elapsed}.");
    }

    private static async Task ForgetPeriodAsync(ExpensesTest test, Guid definitionId, string period)
    {
        await test.ExecuteSqlAsync(
            $"DELETE FROM expenses.Expenses WHERE RecurringDefinitionId = '{definitionId}' AND RecurringPeriod = '{period}'; "
            + $"DELETE FROM expenses.RecurringExpensePeriods WHERE DefinitionId = '{definitionId}' AND Period = '{period}'");
    }

    private static async Task<GenerateRecurringOccurrencesResult> GenerateAsync(ExpensesTest test, Guid definitionId)
    {
        await using var scope = test.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(
            new GenerateRecurringOccurrencesCommand(definitionId, new DateOnly(2026, 11, 20)), TestContext.Current.CancellationToken);
    }
}

/// <summary>Dekorator handlera komendy zadania: błąd wybranej definicji albo akcja po jednostce pracy.</summary>
internal sealed class GenerateHooks
{
    public Func<GenerateRecurringOccurrencesCommand, Task>? Before { get; set; }

    public Func<Task>? After { get; set; }

    public static void Register(IServiceCollection services, GenerateHooks hooks)
    {
        var serviceType = typeof(ICommandHandler<GenerateRecurringOccurrencesCommand, GenerateRecurringOccurrencesResult>);
        var descriptor = services.Single(d => d.ServiceType == serviceType);
        var implementation = descriptor.ImplementationType ?? throw new InvalidOperationException("Handler powinien być zarejestrowany typem.");
        services.Remove(descriptor);
        services.AddScoped(serviceType, provider => new Handler(
            (ICommandHandler<GenerateRecurringOccurrencesCommand, GenerateRecurringOccurrencesResult>)ActivatorUtilities.CreateInstance(provider, implementation),
            hooks));
    }

    private sealed class Handler(ICommandHandler<GenerateRecurringOccurrencesCommand, GenerateRecurringOccurrencesResult> inner, GenerateHooks hooks)
        : ICommandHandler<GenerateRecurringOccurrencesCommand, GenerateRecurringOccurrencesResult>
    {
        public async Task<GenerateRecurringOccurrencesResult> HandleAsync(GenerateRecurringOccurrencesCommand command, CancellationToken cancellationToken)
        {
            if (hooks.Before is { } before)
            {
                await before(command);
            }

            var result = await inner.HandleAsync(command, cancellationToken);
            if (hooks.After is { } after)
            {
                await after();
            }

            return result;
        }
    }
}
