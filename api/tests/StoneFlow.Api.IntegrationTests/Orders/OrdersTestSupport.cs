using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Orders.Domain.Payments;

namespace StoneFlow.Api.IntegrationTests.Orders;

internal sealed record CreatedOrder(Guid Id, string Number, string Version);

internal sealed record AuditRow(string Operation, string AuthorKind, string Label, DateTime BusinessDate, string Changes);

internal sealed record AuditChangeRow(string Field, string? Old, string? New, bool Personal);

/// <summary>Baza testu z migracjami, zalogowany właściciel i pomocnicze wywołania API.</summary>
internal sealed class OrdersTest : IAsyncDisposable
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>Stały zegar aplikacji (5.11.2026 12:00 UTC): numeracja i daty nie zależą od dnia uruchomienia testów.</summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString;

    private OrdersTest(StoneFlowApiFactory factory, AuthClient client, string connectionString)
    {
        Factory = factory;
        Client = client;
        _connectionString = connectionString;
    }

    public StoneFlowApiFactory Factory { get; }

    public AuthClient Client { get; }

    public FakeTimeProvider Time => Factory.Time!;

    /// <summary>
    /// Baza z migracjami i zalogowany właściciel. <paramref name="hooks"/> wstawia przed port
    /// zapisu dekorator, który wykonuje zmianę „z innej karty” w trakcie jednostki pracy
    /// (deterministyczne wyścigi); <paramref name="configureServices"/> pozwala podmienić inne porty.
    /// </summary>
    public static async Task<OrdersTest> CreateAsync(
        SqlServerFixture sqlServer, DateTimeOffset? now = null, StoreHooks? hooks = null, Action<IServiceCollection>? configureServices = null)
    {
        var connectionString = sqlServer.NewDatabase("orders");
        var factory = new StoneFlowApiFactory
        {
            Time = new FakeTimeProvider(now ?? DefaultNow),
            ConfigureTestServices = services =>
            {
                if (hooks is not null)
                {
                    HookedOrderStore.Register(services, hooks);
                }

                configureServices?.Invoke(services);
            },
        }.WithDatabase(connectionString);
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
        var json = await AuthClient.ProblemAsync(response);
        return new CreatedOrder(json.GetProperty("id").GetGuid(), json.GetProperty("number").GetString()!, json.GetProperty("version").GetString()!);
    }

    /// <summary>Udany <c>PUT</c>; zwraca nową wersję.</summary>
    public async Task<string> EditAsync(Guid id, object body)
    {
        using var response = await SendAsync(HttpMethod.Put, $"/api/orders/{id}", body);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (await AuthClient.ProblemAsync(response)).GetProperty("version").GetString()!;
    }

    /// <summary>Udane <c>cancel</c> albo <c>restore</c>; zwraca nową wersję.</summary>
    public async Task<string> StatusActionAsync(Guid id, string action, string version)
    {
        using var response = await SendAsync(HttpMethod.Post, $"/api/orders/{id}/{action}", new { version });
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

    /// <summary>
    /// Zamówienie ręczne wstawione bezpośrednio (osobnym połączeniem, poza jednostką pracy API):
    /// kopia zamówienia <paramref name="sourceId"/> z podanym numerem, kluczem i skrótem żądania.
    /// </summary>
    public async Task<Guid> SeedCopyAsync(Guid sourceId, string number, Guid? creationRequestId, string hashSql)
    {
        var id = Guid.NewGuid();
        var key = creationRequestId is { } value ? $"'{value}'" : "NULL";
        await ExecuteSqlAsync(
            "INSERT INTO orders.Orders (Id, Number, Source, Channel, OrderDate, Value, FulfillmentStatus, CustomerKind, CustomerName, "
            + "Suppliers, ZoReferences, HasPhotos, HasReview, CreatedAtUtc, UpdatedAtUtc, CreationRequestId, CreationRequestHash) "
            + $"SELECT '{id}', N'{number}', N'Manual', Channel, OrderDate, Value, FulfillmentStatus, CustomerKind, CustomerName, "
            + $"Suppliers, ZoReferences, HasPhotos, HasReview, CreatedAtUtc, UpdatedAtUtc, {key}, {hashSql} "
            + $"FROM orders.Orders WHERE Id = '{sourceId}';");
        return id;
    }

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

    /// <summary>Wiersze zapytania SQL odczytane osobnym połączeniem.</summary>
    public async Task<IReadOnlyList<T>> RowsAsync<T>(string sql, Func<SqlDataReader, T> map)
    {
        await using var connection = await OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(map(reader));
        }

        return rows;
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

/// <summary>Akcje wykonywane przez dekorator portu zapisu tuż przed dodaniem albo zapisem zamówienia.</summary>
internal sealed class StoreHooks
{
    public Func<Order, Task>? BeforeAdd { get; set; }

    public Func<Order, Task>? BeforeSave { get; set; }
}

/// <summary>Dekorator prawdziwego <see cref="IOrderStore"/> (tylko w testach).</summary>
internal sealed class HookedOrderStore(IOrderStore inner, StoreHooks hooks) : IOrderStore
{
    public static void Register(IServiceCollection services, StoreHooks hooks)
    {
        var descriptor = services.Single(d => d.ServiceType == typeof(IOrderStore));
        var implementation = descriptor.ImplementationType
            ?? throw new InvalidOperationException("Port zapisu zamówień powinien być zarejestrowany typem.");
        services.Remove(descriptor);
        services.AddScoped<IOrderStore>(provider =>
            new HookedOrderStore((IOrderStore)ActivatorUtilities.CreateInstance(provider, implementation), hooks));
    }

    public Task<StoredOrder?> LoadAsync(Guid id, CancellationToken cancellationToken) => inner.LoadAsync(id, cancellationToken);

    public async Task<StoreSaveResult> AddAsync(Order order, CancellationToken cancellationToken)
    {
        if (hooks.BeforeAdd is { } beforeAdd)
        {
            await beforeAdd(order);
        }

        return await inner.AddAsync(order, cancellationToken);
    }

    public async Task<StoreSaveResult> SaveAsync(Order order, byte[] expectedVersion, CancellationToken cancellationToken)
    {
        if (hooks.BeforeSave is { } beforeSave)
        {
            await beforeSave(order);
        }

        return await inner.SaveAsync(order, expectedVersion, cancellationToken);
    }
}

/// <summary>Akcje wykonywane przez dekorator portu zapisu wpisów tuż przed dodaniem, zapisem albo usunięciem.</summary>
internal sealed class EntryStoreHooks
{
    public Func<OrderEntry, Task>? BeforeAdd { get; set; }

    public Func<OrderEntry, Task>? BeforeSave { get; set; }

    public Func<OrderEntry, Task>? BeforeDelete { get; set; }

    /// <summary>Podmienia porty wpłat i zwrotów na dekoratory z tymi akcjami.</summary>
    public void Register(IServiceCollection services)
    {
        HookedEntryStore<Payment>.Register(services, this);
        HookedEntryStore<Refund>.Register(services, this);
    }
}

/// <summary>Dekorator prawdziwego <see cref="IOrderEntryStore{TEntry}"/> (tylko w testach).</summary>
internal sealed class HookedEntryStore<TEntry>(IOrderEntryStore<TEntry> inner, EntryStoreHooks hooks) : IOrderEntryStore<TEntry>
    where TEntry : OrderEntry
{
    public Task<StoredEntry<TEntry>?> LoadAsync(Guid id, CancellationToken cancellationToken) => inner.LoadAsync(id, cancellationToken);

    public async Task<StoreSaveResult> AddAsync(TEntry entry, CancellationToken cancellationToken)
    {
        if (hooks.BeforeAdd is { } beforeAdd)
        {
            await beforeAdd(entry);
        }

        return await inner.AddAsync(entry, cancellationToken);
    }

    public async Task<StoreSaveResult> SaveAsync(TEntry entry, byte[] expectedVersion, CancellationToken cancellationToken)
    {
        if (hooks.BeforeSave is { } beforeSave)
        {
            await beforeSave(entry);
        }

        return await inner.SaveAsync(entry, expectedVersion, cancellationToken);
    }

    public async Task<StoreSaveResult> DeleteAsync(TEntry entry, byte[] expectedVersion, CancellationToken cancellationToken)
    {
        if (hooks.BeforeDelete is { } beforeDelete)
        {
            await beforeDelete(entry);
        }

        return await inner.DeleteAsync(entry, expectedVersion, cancellationToken);
    }

    public Task<EntryCreationMatch?> FindByCreationRequestAsync(Guid creationRequestId, CancellationToken cancellationToken) =>
        inner.FindByCreationRequestAsync(creationRequestId, cancellationToken);

    public static void Register(IServiceCollection services, EntryStoreHooks hooks)
    {
        var descriptor = services.Single(d => d.ServiceType == typeof(IOrderEntryStore<TEntry>));
        var implementation = descriptor.ImplementationType
            ?? throw new InvalidOperationException("Port zapisu wpisów powinien być zarejestrowany typem.");
        services.Remove(descriptor);
        services.AddScoped<IOrderEntryStore<TEntry>>(provider =>
            new HookedEntryStore<TEntry>((IOrderEntryStore<TEntry>)ActivatorUtilities.CreateInstance(provider, implementation), hooks));
    }
}
