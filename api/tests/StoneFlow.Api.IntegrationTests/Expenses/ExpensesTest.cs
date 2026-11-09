using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Domain.Expenses;

namespace StoneFlow.Api.IntegrationTests.Expenses;

internal sealed record CreatedExpense(Guid Id, string Version);

internal sealed record ExpenseAuditRow(string Operation, string AuthorKind, string Label, DateTime BusinessDate, string Changes)
{
    public IReadOnlyList<(string Field, string? Old, string? New, bool Personal)> ChangeList =>
    [
        .. JsonDocument.Parse(Changes).RootElement.EnumerateArray().Select(c => (
            c.GetProperty("field").GetString()!,
            c.GetProperty("old").GetString(),
            c.GetProperty("new").GetString(),
            c.GetProperty("personal").GetBoolean())),
    ];
}

/// <summary>Baza testu z migracjami, zalogowany właściciel i pomocnicze wywołania <c>/api/expenses</c>.</summary>
internal sealed class ExpensesTest : IAsyncDisposable
{
    /// <summary>Stały zegar aplikacji: 5.11.2026 12:00 UTC, czyli „dzisiaj” 5.11.2026 w Europe/Warsaw.</summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString;

    private ExpensesTest(StoneFlowApiFactory factory, AuthClient client, string connectionString)
    {
        Factory = factory;
        Client = client;
        _connectionString = connectionString;
    }

    public StoneFlowApiFactory Factory { get; }

    public AuthClient Client { get; }

    public FakeTimeProvider Time => Factory.Time!;

    /// <summary>
    /// Baza z migracjami i zalogowany właściciel. <paramref name="hooks"/> wstawia przed port zapisu
    /// dekorator, który wykonuje zmianę „z innej karty” w trakcie jednostki pracy (deterministyczny wyścig).
    /// </summary>
    public static async Task<ExpensesTest> CreateAsync(
        SqlServerFixture sqlServer, DateTimeOffset? now = null, ExpenseStoreHooks? hooks = null, Action<IServiceCollection>? configureServices = null)
    {
        var connectionString = sqlServer.NewDatabase("expenses");
        var factory = new StoneFlowApiFactory
        {
            Time = new FakeTimeProvider(now ?? DefaultNow),
            ConfigureTestServices = services =>
            {
                if (hooks is not null)
                {
                    HookedExpenseStore.Register(services, hooks);
                }

                configureServices?.Invoke(services);
            },
        }.WithDatabase(connectionString);
        await TestDatabase.MigrateAsync(factory);
        var client = AuthClient.Create(factory);
        await client.SetUpOwnerAsync(factory);
        using var login = await client.LoginAsync();
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return new ExpensesTest(factory, client, connectionString);
    }

    /// <summary>Dane wydatku jak z formularza (fikcyjne).</summary>
    public static Dictionary<string, object?> Input(
        string? description = "Cięcie płyt",
        string? category = "cutting",
        string? counterparty = "Dostawca A",
        string? costDate = "2026-10-28",
        string? dueDate = "2026-11-10",
        string? paidOn = null,
        string? grossAmount = "1234.50",
        string? netAmount = "1003.66",
        string? documentReference = "FV 12/2026",
        string? paymentMethod = "transfer",
        string? payer = null,
        string? comment = null,
        string? version = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["description"] = description,
            ["category"] = category,
            ["counterparty"] = counterparty,
            ["costDate"] = costDate,
            ["dueDate"] = dueDate,
            ["paidOn"] = paidOn,
            ["grossAmount"] = grossAmount,
            ["netAmount"] = netAmount,
            ["documentReference"] = documentReference,
            ["paymentMethod"] = paymentMethod,
            ["payer"] = payer,
            ["comment"] = comment,
        };
        if (version is not null)
        {
            body["version"] = version;
        }

        return body;
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
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await Client.Http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public Task<HttpResponseMessage> CreateAsync(object body, Guid? key = null) =>
        SendAsync(HttpMethod.Post, "/api/expenses", body, (key ?? Guid.NewGuid()).ToString());

    public async Task<CreatedExpense> CreateExpenseAsync(object body, Guid? key = null)
    {
        using var response = await CreateAsync(body, key);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var json = await AuthClient.ProblemAsync(response);
        return new CreatedExpense(json.GetProperty("id").GetGuid(), json.GetProperty("version").GetString()!);
    }

    /// <summary>Udany zapis zwracający nową wersję (<c>PUT</c>, <c>mark-paid</c>, <c>unmark-paid</c>).</summary>
    public async Task<string> ChangeAsync(HttpMethod method, string path, object body)
    {
        using var response = await SendAsync(method, path, body);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (await AuthClient.ProblemAsync(response)).GetProperty("version").GetString()!;
    }

    public async Task<JsonElement> GetJsonAsync(string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return await AuthClient.ProblemAsync(response);
    }

    public async Task<Guid[]> IdsAsync(string path) =>
        [.. (await GetJsonAsync(path)).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())];

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

    public async Task<IReadOnlyList<ExpenseAuditRow>> AuditRowsAsync(Guid expenseId)
    {
        await using var connection = await OpenAsync();
        await using var command = new SqlCommand(
            "SELECT Operation, AuthorKind, ObjectLabel, ObjectBusinessDate, Changes FROM reporting.AuditEntries "
            + "WHERE ObjectType = N'Expense' AND ObjectId = @id ORDER BY Id",
            connection);
        command.Parameters.AddWithValue("@id", expenseId);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var rows = new List<ExpenseAuditRow>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(new ExpenseAuditRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetDateTime(3), reader.GetString(4)));
        }

        return rows;
    }

    /// <summary>Przestawia zegar aplikacji (tylko do przodu) i loguje ponownie, bo sesja wygasa po godzinie bezczynności.</summary>
    public async Task AdvanceToAsync(DateTimeOffset instant)
    {
        Time.SetUtcNow(instant);
        using var login = await Client.LoginAsync();
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    public async Task<IReadOnlyList<T>> RowsAsync<T>(string sql, Func<SqlDataReader, T> read)
    {
        await using var connection = await OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(read(reader));
        }

        return rows;
    }

    public string ConnectionString => _connectionString;

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

/// <summary>Akcje wykonywane przez dekorator portu zapisu tuż przed zapisem albo usunięciem wydatku.</summary>
internal sealed class ExpenseStoreHooks
{
    public Func<Expense, Task>? BeforeSave { get; set; }

    public Func<Expense, Task>? BeforeDelete { get; set; }
}

/// <summary>Dekorator prawdziwego <see cref="IExpenseStore"/> (tylko w testach).</summary>
internal sealed class HookedExpenseStore(IExpenseStore inner, ExpenseStoreHooks hooks) : IExpenseStore
{
    public static void Register(IServiceCollection services, ExpenseStoreHooks hooks)
    {
        var descriptor = services.Single(d => d.ServiceType == typeof(IExpenseStore));
        var implementation = descriptor.ImplementationType
            ?? throw new InvalidOperationException("Port zapisu wydatków powinien być zarejestrowany typem.");
        services.Remove(descriptor);
        services.AddScoped<IExpenseStore>(provider =>
            new HookedExpenseStore((IExpenseStore)ActivatorUtilities.CreateInstance(provider, implementation), hooks));
    }

    public Task<StoredExpense?> LoadAsync(Guid id, CancellationToken cancellationToken) => inner.LoadAsync(id, cancellationToken);

    public Task<StoreSaveResult> AddAsync(Expense expense, CancellationToken cancellationToken) => inner.AddAsync(expense, cancellationToken);

    public async Task<StoreSaveResult> SaveAsync(Expense expense, byte[] expectedVersion, CancellationToken cancellationToken)
    {
        if (hooks.BeforeSave is { } beforeSave)
        {
            await beforeSave(expense);
        }

        return await inner.SaveAsync(expense, expectedVersion, cancellationToken);
    }

    public async Task<StoreSaveResult> DeleteAsync(Expense expense, byte[] expectedVersion, CancellationToken cancellationToken)
    {
        if (hooks.BeforeDelete is { } beforeDelete)
        {
            await beforeDelete(expense);
        }

        return await inner.DeleteAsync(expense, expectedVersion, cancellationToken);
    }
}
