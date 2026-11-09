using System.Net;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Domain.Recurring;
using StoneFlow.Expenses.Infrastructure.Persistence;
using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Api.IntegrationTests.Expenses;

internal sealed record CreatedRecurring(Guid Id, string Version, int CreatedOccurrences);

/// <summary>
/// Pomocnicze wywołania <c>/api/recurring-expenses</c> i zadania <c>recurring-expenses</c> na bazie
/// testu wydatków. Zegar aplikacji ustawia się na „dzisiaj” (10:00 UTC, ten sam dzień w Warszawie).
/// Dane fikcyjne.
/// </summary>
internal static class Recurring
{
    public const string Base = "/api/recurring-expenses";
    public const string JobKey = "recurring-expenses";

    public static DateTimeOffset At(int day, int month, int year = 2026, int hour = 10) => new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    public static Task<ExpensesTest> CreateTestAsync(
        SqlServerFixture sqlServer, DateTimeOffset now, RecurringStoreHooks? hooks = null, Action<IServiceCollection>? configureServices = null) =>
        ExpensesTest.CreateAsync(
            sqlServer,
            now,
            configureServices: services =>
            {
                if (hooks is not null)
                {
                    HookedRecurringExpenseStore.Register(services, hooks);
                }

                configureServices?.Invoke(services);
            });

    public static Dictionary<string, object?> Input(
        int? dayOfMonth = 10,
        string? startsOn = "2026-09-01",
        string? endsOn = null,
        string? grossAmount = "300.00",
        string? netAmount = "243.90",
        string? description = "Abonament",
        string? category = "marketing",
        string? counterparty = "Dostawca B",
        string? paymentMethod = "transfer",
        string? payer = null,
        string? version = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["description"] = description,
            ["category"] = category,
            ["counterparty"] = counterparty,
            ["grossAmount"] = grossAmount,
            ["netAmount"] = netAmount,
            ["paymentMethod"] = paymentMethod,
            ["payer"] = payer,
            ["comment"] = null,
            ["dayOfMonth"] = dayOfMonth,
            ["startsOn"] = startsOn,
            ["endsOn"] = endsOn,
        };
        if (version is not null)
        {
            body["version"] = version;
        }

        return body;
    }

    public static Task<HttpResponseMessage> PostAsync(this ExpensesTest test, object body, Guid? key = null) =>
        test.SendAsync(HttpMethod.Post, Base, body, (key ?? Guid.NewGuid()).ToString());

    public static async Task<CreatedRecurring> CreateRecurringAsync(this ExpensesTest test, object body, Guid? key = null)
    {
        using var response = await test.PostAsync(body, key);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var json = await AuthClient.ProblemAsync(response);
        return new CreatedRecurring(json.GetProperty("id").GetGuid(), json.GetProperty("version").GetString()!, json.GetProperty("createdOccurrences").GetInt32());
    }

    public static Task<JsonElement> DetailsAsync(this ExpensesTest test, Guid id) => test.GetJsonAsync($"{Base}/{id}");

    public static async Task<string> VersionAsync(this ExpensesTest test, Guid id) =>
        (await test.DetailsAsync(id)).GetProperty("version").GetString()!;

    /// <summary><c>PUT</c> z aktualną wersją; zwraca odpowiedź do sprawdzenia przez wywołującego.</summary>
    public static async Task<HttpResponseMessage> PutAsync(this ExpensesTest test, Guid id, Dictionary<string, object?> body, string? version = null)
    {
        body["version"] = version ?? await test.VersionAsync(id);
        return await test.SendAsync(HttpMethod.Put, $"{Base}/{id}", body);
    }

    /// <summary>Udana zmiana (<c>PUT</c>, <c>pause</c>, <c>resume</c>): liczba utworzonych wystąpień i nowa wersja.</summary>
    public static async Task<(int Created, string Version)> OkAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var json = await AuthClient.ProblemAsync(response);
            return (json.GetProperty("createdOccurrences").GetInt32(), json.GetProperty("version").GetString()!);
        }
    }

    public static async Task<HttpResponseMessage> ActionAsync(this ExpensesTest test, Guid id, string action, string? version = null) =>
        await test.SendAsync(HttpMethod.Post, $"{Base}/{id}/{action}", new Dictionary<string, object?> { ["version"] = version ?? await test.VersionAsync(id) });

    public static async Task<string> ProblemCodeAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        using (response)
        {
            Assert.True(response.StatusCode == status, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
            var json = await AuthClient.ProblemAsync(response);
            return json.TryGetProperty("code", out var code) ? code.GetString()! : string.Join(",", json.GetProperty("errors").EnumerateObject().Select(e => e.Name));
        }
    }

    public static Task<JsonElement> PreviewAsync(this ExpensesTest test, string query) => test.GetJsonAsync($"{Base}/preview?{query}");

    /// <summary>Terminy (data kosztu) istniejących wystąpień definicji, rosnąco, jako <c>yyyy-MM-dd</c>.</summary>
    public static Task<IReadOnlyList<string>> OccurrencesAsync(this ExpensesTest test, Guid definitionId) =>
        test.RowsAsync(
            $"SELECT CONVERT(char(10), CostDate, 23) FROM expenses.Expenses WHERE RecurringDefinitionId = '{definitionId}' ORDER BY CostDate",
            reader => reader.GetString(0));

    /// <summary>Przebieg zadania przez runner harmonogramu (ta sama dzierżawa co o 00:10).</summary>
    public static async Task<JobRunStatus> RunJobAsync(this ExpensesTest test, CancellationToken? cancellationToken = null)
    {
        var outcome = await test.Factory.Services.GetRequiredService<IJobRunner>()
            .TryRunNowAsync(JobKey, cancellationToken ?? TestContext.Current.CancellationToken);
        return Assert.IsType<JobRunOutcome.Completed>(outcome).Status;
    }

    public static Task<IReadOnlyList<string>> LastJobErrorAsync(this ExpensesTest test) =>
        test.RowsAsync(
            $"SELECT TOP 1 ISNULL(Error, N'') FROM scheduling.JobRuns WHERE JobKey = N'{JobKey}' ORDER BY StartedAtUtc DESC",
            reader => reader.GetString(0));
}

/// <summary>Akcje dekoratora portu zapisu definicji wykonywane przed zapisem wsadu wystąpień.</summary>
internal sealed class RecurringStoreHooks
{
    private int _calls;

    /// <summary>Numer wywołania (od 1) i wsad; kontekst modułu pozwala pisać w tej samej transakcji.</summary>
    public Func<int, ExpensesDbContext, IReadOnlyList<RecurringOccurrence>, Task>? BeforeAddOccurrences { get; set; }

    public int Calls => _calls;

    internal int NextCall() => Interlocked.Increment(ref _calls);
}

/// <summary>Dekorator prawdziwego <see cref="IRecurringExpenseStore"/> (tylko w testach).</summary>
internal sealed class HookedRecurringExpenseStore(IRecurringExpenseStore inner, ExpensesDbContext context, RecurringStoreHooks hooks) : IRecurringExpenseStore
{
    public static void Register(IServiceCollection services, RecurringStoreHooks hooks)
    {
        var descriptor = services.Single(d => d.ServiceType == typeof(IRecurringExpenseStore));
        var implementation = descriptor.ImplementationType
            ?? throw new InvalidOperationException("Port zapisu definicji powinien być zarejestrowany typem.");
        services.Remove(descriptor);
        services.AddScoped<IRecurringExpenseStore>(provider => new HookedRecurringExpenseStore(
            (IRecurringExpenseStore)ActivatorUtilities.CreateInstance(provider, implementation),
            provider.GetRequiredService<ExpensesDbContext>(),
            hooks));
    }

    public Task<StoredRecurringDefinition?> LoadAsync(Guid id, CancellationToken cancellationToken) => inner.LoadAsync(id, cancellationToken);

    public Task<IReadOnlyDictionary<RecurringPeriod, DateOnly>> RegisteredPeriodsAsync(Guid definitionId, CancellationToken cancellationToken) =>
        inner.RegisteredPeriodsAsync(definitionId, cancellationToken);

    public Task<StoreSaveResult> AddAsync(RecurringExpenseDefinition definition, CancellationToken cancellationToken) => inner.AddAsync(definition, cancellationToken);

    public Task<StoreSaveResult> SaveAsync(RecurringExpenseDefinition definition, byte[] expectedVersion, CancellationToken cancellationToken) =>
        inner.SaveAsync(definition, expectedVersion, cancellationToken);

    public Task<StoreSaveResult> DeleteAsync(RecurringExpenseDefinition definition, byte[] expectedVersion, CancellationToken cancellationToken) =>
        inner.DeleteAsync(definition, expectedVersion, cancellationToken);

    public async Task<OccurrenceSaveStatus> AddOccurrencesAsync(IReadOnlyList<RecurringOccurrence> occurrences, CancellationToken cancellationToken)
    {
        if (hooks.BeforeAddOccurrences is { } before)
        {
            await before(hooks.NextCall(), context, occurrences);
        }

        return await inner.AddOccurrencesAsync(occurrences, cancellationToken);
    }

    public Task<StoreSaveResult> AddRequestAsync(RecurringDefinitionRequest request, CancellationToken cancellationToken) => inner.AddRequestAsync(request, cancellationToken);
}

/// <summary>Zapisy „z zewnątrz” w transakcji jednostki pracy (przez kontekst modułu) i prawdziwy wyjątek zakleszczenia.</summary>
internal static class RecurringRaces
{
    /// <summary>Wiersz rejestru dla okresu pierwszego wystąpienia wsadu: zapis wsadu narusza <c>PK_RecurringExpensePeriods</c>.</summary>
    public static Task InsertConflictingPeriodAsync(ExpensesDbContext context, RecurringOccurrence occurrence) =>
        context.Database.ExecuteSqlAsync(
            $"INSERT INTO [expenses].[RecurringExpensePeriods] ([DefinitionId], [Period], [DueDate], [ExpenseId], [CreatedAtUtc]) VALUES ({occurrence.Period.DefinitionId}, {occurrence.Period.Period}, {occurrence.Period.DueDate}, {Guid.NewGuid()}, SYSUTCDATETIME())");

    /// <summary>
    /// Prawdziwy <see cref="SqlException"/> 1205: dwa połączenia blokują dwa wiersze w odwrotnej
    /// kolejności, a ofiarą jest połączenie z niskim priorytetem zakleszczenia.
    /// </summary>
    public static async Task<SqlException> DeadlockAsync(string connectionString)
    {
        var token = TestContext.Current.CancellationToken;
        await using (var setup = new SqlConnection(connectionString))
        {
            await setup.OpenAsync(token);
            await using var create = new SqlCommand(
                "IF OBJECT_ID(N'dbo.DeadlockProbe') IS NULL BEGIN CREATE TABLE dbo.DeadlockProbe (Id int PRIMARY KEY, V int NOT NULL); INSERT dbo.DeadlockProbe VALUES (1, 0), (2, 0); END",
                setup);
            await create.ExecuteNonQueryAsync(token);
        }

        await using var winner = new SqlConnection(connectionString);
        await using var victim = new SqlConnection(connectionString);
        await winner.OpenAsync(token);
        await victim.OpenAsync(token);
        await using var winnerTransaction = (SqlTransaction)await winner.BeginTransactionAsync(token);
        await using (var priority = new SqlCommand("SET DEADLOCK_PRIORITY LOW", victim))
        {
            await priority.ExecuteNonQueryAsync(token);
        }

        await using var victimTransaction = (SqlTransaction)await victim.BeginTransactionAsync(token);
        await ExecuteAsync(winner, winnerTransaction, "UPDATE dbo.DeadlockProbe SET V = V + 1 WHERE Id = 1");
        await ExecuteAsync(victim, victimTransaction, "UPDATE dbo.DeadlockProbe SET V = V + 1 WHERE Id = 2");
        var blocked = ExecuteAsync(winner, winnerTransaction, "UPDATE dbo.DeadlockProbe SET V = V + 1 WHERE Id = 2");
        await Task.Delay(TimeSpan.FromMilliseconds(500), token);
        try
        {
            await ExecuteAsync(victim, victimTransaction, "UPDATE dbo.DeadlockProbe SET V = V + 1 WHERE Id = 1");
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            await blocked;
            await winnerTransaction.RollbackAsync(token);
            return exception;
        }

        throw new InvalidOperationException("Nie udało się wywołać zakleszczenia.");
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
