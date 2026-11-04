using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Application.Persistence;
using StoneFlow.Shared.Infrastructure.Persistence;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Api.IntegrationTests.Audit;

/// <summary>
/// AUD-01 / SF-7: zapis audytu w transakcji zmiany danych modułu. Moduł testowy (<see cref="ProbeDbContext"/>,
/// schemat <c>probe</c>) zastępuje przyszłe moduły finansowe i korzysta z tej samej rejestracji kontekstu.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class AuditLogTests(SqlServerFixture sqlServer)
{
    private static readonly Guid AccountId = Guid.Parse("4c2f8a3e-1b7d-4e55-9a0f-2d6b1c3e5f70");
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Commit_saves_data_and_complete_audit_entry_together()
    {
        await using var test = await ProbeDatabase.CreateAsync(sqlServer);
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        using (var scope = test.Factory.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
                async cancellationToken =>
                {
                    var probe = services.GetRequiredService<ProbeDbContext>();
                    probe.Items.Add(new ProbeItem { Id = paymentId, Name = "wpłata" });
                    await probe.SaveChangesAsync(cancellationToken);
                    await services.GetRequiredService<IAuditLog>().RecordAsync(
                        new AuditEntry(
                            AuditAuthor.Account(AccountId),
                            new AuditObject(AuditObjectType.Payment, paymentId, new DateOnly(2026, 10, 30), "Wpłata do ZAM/2026/0042",
                                new AuditObjectReference(AuditObjectType.Order, orderId)),
                            AuditOperation.Created,
                            [new AuditChange("amount", null, "1234.50"), new AuditChange("payerName", null, "Jan Kowalski", IsPersonalData: true)]),
                        cancellationToken);
                    return true;
                },
                commitWhen: _ => true,
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, await test.CountItemsAsync());
        var row = Assert.Single(await test.AuditRowsAsync());
        Assert.Equal(Now.UtcDateTime, row.OccurredAtUtc);
        Assert.Equal("Account", row.AuthorKind);
        Assert.Equal(AccountId, row.AuthorAccountId);
        Assert.Equal("Payment", row.ObjectType);
        Assert.Equal(paymentId, row.ObjectId);
        Assert.Equal(new DateTime(2026, 10, 30), row.ObjectBusinessDate);
        Assert.Equal("Wpłata do ZAM/2026/0042", row.ObjectLabel);
        Assert.Equal("Order", row.ParentObjectType);
        Assert.Equal(orderId, row.ParentObjectId);
        Assert.Equal("Created", row.Operation);

        using var changes = JsonDocument.Parse(row.Changes);
        var items = changes.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal("amount", items[0].GetProperty("field").GetString());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("old").ValueKind);
        Assert.Equal("1234.50", items[0].GetProperty("new").GetString());
        Assert.False(items[0].GetProperty("personal").GetBoolean());
        Assert.True(items[1].GetProperty("personal").GetBoolean());
    }

    [Fact]
    public async Task System_author_is_stored_without_account()
    {
        await using var test = await ProbeDatabase.CreateAsync(sqlServer);

        await test.RunAsync(async (services, cancellationToken) =>
        {
            await services.GetRequiredService<IAuditLog>().RecordAsync(Entry(AuditAuthor.WooCommerceSync), cancellationToken);
            return true;
        });

        var row = Assert.Single(await test.AuditRowsAsync());
        Assert.Equal("WooCommerceSync", row.AuthorKind);
        Assert.Null(row.AuthorAccountId);
    }

    [Fact]
    public async Task Invalid_audit_entry_rolls_back_data_change()
    {
        await using var test = await ProbeDatabase.CreateAsync(sqlServer);

        await Assert.ThrowsAsync<ArgumentException>(() => test.RunAsync(async (services, cancellationToken) =>
        {
            await test.AddItemAsync(services, cancellationToken);
            await services.GetRequiredService<IAuditLog>().RecordAsync(Entry(label: " "), cancellationToken);
            return true;
        }));

        Assert.Equal(0, await test.CountItemsAsync());
        Assert.Empty(await test.AuditRowsAsync());
    }

    [Fact]
    public async Task Database_failure_of_audit_rolls_back_data_change()
    {
        await using var test = await ProbeDatabase.CreateAsync(sqlServer);
        // Ograniczenie odrzucające każdy wpis: prawdziwy błąd bazy przy zapisie audytu.
        await test.ExecuteSqlAsync(
            "ALTER TABLE reporting.AuditEntries ADD CONSTRAINT CK_Test_RejectAll CHECK (ObjectLabel = N'nigdy');");

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => test.RunAsync(async (services, cancellationToken) =>
        {
            await test.AddItemAsync(services, cancellationToken);
            await services.GetRequiredService<IAuditLog>().RecordAsync(Entry(), cancellationToken);
            return true;
        }));

        Assert.Equal(0, await test.CountItemsAsync());
    }

    [Fact]
    public async Task Failed_data_change_rolls_back_audit_entry()
    {
        await using var test = await ProbeDatabase.CreateAsync(sqlServer);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => test.RunAsync(async (services, cancellationToken) =>
        {
            await services.GetRequiredService<IAuditLog>().RecordAsync(Entry(), cancellationToken);
            await test.AddItemAsync(services, cancellationToken, name: new string('x', 51)); // przekracza nvarchar(50)
            return true;
        }));

        Assert.Empty(await test.AuditRowsAsync());
        Assert.Equal(0, await test.CountItemsAsync());
    }

    [Fact]
    public async Task Audit_outside_unit_of_work_is_rejected()
    {
        await using var test = await ProbeDatabase.CreateAsync(sqlServer);

        using (var scope = test.Factory.Services.CreateScope())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IAuditLog>()
                .RecordAsync(Entry(), TestContext.Current.CancellationToken));
        }

        Assert.Empty(await test.AuditRowsAsync());
    }

    [Fact]
    public async Task Commit_when_false_rolls_back_and_returns_result()
    {
        await using var test = await ProbeDatabase.CreateAsync(sqlServer);

        var result = await test.RunAsync(
            async (services, cancellationToken) =>
            {
                await test.AddItemAsync(services, cancellationToken);
                await services.GetRequiredService<IAuditLog>().RecordAsync(Entry(), cancellationToken);
                return false;
            },
            commitWhen: succeeded => succeeded);

        Assert.False(result);
        Assert.Equal(0, await test.CountItemsAsync());
        Assert.Empty(await test.AuditRowsAsync());
    }

    [Fact]
    public async Task Contexts_created_before_and_during_transaction_share_it_for_query_update_and_save()
    {
        await using var test = await ProbeDatabase.CreateAsync(sqlServer);
        var existing = Guid.NewGuid();
        await test.ExecuteSqlAsync($"INSERT INTO probe.Items (Id, Name) VALUES ('{existing}', N'przed');");
        var cancellationToken = TestContext.Current.CancellationToken;

        using var scope = test.Factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var before = services.GetRequiredService<ProbeDbContext>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync<bool>(
            async ct =>
            {
                Assert.Equal(1, await before.Items.CountAsync(ct));
                await before.Items.Where(item => item.Id == existing)
                    .ExecuteUpdateAsync(set => set.SetProperty(item => item.Name, "w transakcji"), ct);

                await using var during = new ProbeDbContext(services.GetRequiredService<DbContextOptions<ProbeDbContext>>());
                Assert.Equal("w transakcji", await during.Items.Where(item => item.Id == existing).Select(item => item.Name).SingleAsync(ct));
                during.Items.Add(new ProbeItem { Id = Guid.NewGuid(), Name = "nowy" });
                await during.SaveChangesAsync(ct);
                before.Items.Add(new ProbeItem { Id = Guid.NewGuid(), Name = "drugi" });
                await before.SaveChangesAsync(ct);
                await services.GetRequiredService<IAuditLog>().RecordAsync(Entry(), ct);

                throw new InvalidOperationException("wycofaj");
            },
            commitWhen: _ => true,
            cancellationToken));

        Assert.Equal(1, await test.CountItemsAsync());
        Assert.Equal("przed", await test.ScalarAsync<string>($"SELECT Name FROM probe.Items WHERE Id = '{existing}'"));
        Assert.Empty(await test.AuditRowsAsync());

        // Ta sama instancja kontekstu po wycofanej jednostce pracy (śledzone encje porzucone):
        // kolejna jednostka pracy działa i nie zapisuje encji z wycofanej próby.
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                before.Items.Add(new ProbeItem { Id = Guid.NewGuid(), Name = "po" });
                await before.SaveChangesAsync(ct);
                await services.GetRequiredService<IAuditLog>().RecordAsync(Entry(), ct);
                return true;
            },
            commitWhen: _ => true,
            cancellationToken);

        Assert.Equal(2, await test.CountItemsAsync());
        Assert.Single(await test.AuditRowsAsync());
    }

    [Fact]
    public async Task Save_with_several_statements_joins_the_unit_of_work_transaction()
    {
        // SaveChanges z kilkoma poleceniami (UPDATE + INSERT) otwiera w EF własną transakcję — bez
        // dołączenia kontekstu (UseTransaction w interceptorze) SqlClient odrzuciłby transakcję
        // równoległą. Same wstawienia do jednej tabeli EF skleja w jedno polecenie bez transakcji.
        await using var test = await ProbeDatabase.CreateAsync(sqlServer);
        var existing = Guid.NewGuid();
        await test.ExecuteSqlAsync($"INSERT INTO probe.Items (Id, Name) VALUES ('{existing}', N'przed');");

        await Assert.ThrowsAsync<InvalidOperationException>(() => test.RunAsync<bool>(async (services, cancellationToken) =>
        {
            await ChangeAndAddAsync(services, existing, "w transakcji", cancellationToken);
            await services.GetRequiredService<IAuditLog>().RecordAsync(Entry(), cancellationToken);
            throw new InvalidOperationException("wycofaj");
        }));

        Assert.Equal(1, await test.CountItemsAsync());
        Assert.Equal("przed", await test.ScalarAsync<string>($"SELECT Name FROM probe.Items WHERE Id = '{existing}'"));
        Assert.Empty(await test.AuditRowsAsync());

        await test.RunAsync(async (services, cancellationToken) =>
        {
            await ChangeAndAddAsync(services, existing, "po", cancellationToken);
            await services.GetRequiredService<IAuditLog>().RecordAsync(Entry(), cancellationToken);
            await services.GetRequiredService<IAuditLog>().RecordAsync(Entry(), cancellationToken);
            return true;
        });

        Assert.Equal(2, await test.CountItemsAsync());
        Assert.Equal("po", await test.ScalarAsync<string>($"SELECT Name FROM probe.Items WHERE Id = '{existing}'"));
        Assert.Equal(2, (await test.AuditRowsAsync()).Count);

        static async Task ChangeAndAddAsync(IServiceProvider services, Guid id, string name, CancellationToken cancellationToken)
        {
            var probe = services.GetRequiredService<ProbeDbContext>();
            var item = await probe.Items.SingleAsync(entity => entity.Id == id, cancellationToken);
            item.Name = name;
            probe.Items.Add(new ProbeItem { Id = Guid.NewGuid(), Name = "nowa" });
            await probe.SaveChangesAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Cancellation_rolls_back_data_and_audit()
    {
        await using var test = await ProbeDatabase.CreateAsync(sqlServer);
        using var cancellation = new CancellationTokenSource();

        using (var scope = test.Factory.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => services.GetRequiredService<IUnitOfWork>()
                .ExecuteInTransactionAsync<bool>(
                    async cancellationToken =>
                    {
                        await test.AddItemAsync(services, cancellationToken);
                        await services.GetRequiredService<IAuditLog>().RecordAsync(Entry(), cancellationToken);
                        await cancellation.CancelAsync();
                        cancellationToken.ThrowIfCancellationRequested();
                        return true;
                    },
                    commitWhen: _ => true,
                    cancellation.Token));
        }

        Assert.Equal(0, await test.CountItemsAsync());
        Assert.Empty(await test.AuditRowsAsync());
    }

    [Fact]
    public async Task Nested_unit_of_work_is_rejected()
    {
        await using var test = await ProbeDatabase.CreateAsync(sqlServer);

        await Assert.ThrowsAsync<InvalidOperationException>(() => test.RunAsync((services, cancellationToken) =>
            services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(_ => Task.FromResult(true), _ => true, cancellationToken)));
    }

    [Fact]
    public async Task Database_rejects_account_author_without_account_and_parent_without_id()
    {
        await using var test = await ProbeDatabase.CreateAsync(sqlServer);
        const string Insert =
            "INSERT INTO reporting.AuditEntries (OccurredAtUtc, AuthorKind, AuthorAccountId, ObjectType, ObjectId, ObjectBusinessDate, ObjectLabel, ParentObjectType, ParentObjectId, Operation, Changes) ";

        await Assert.ThrowsAsync<SqlException>(() => test.ExecuteSqlAsync(
            Insert + $"VALUES (SYSUTCDATETIME(), N'Account', NULL, N'Order', '{Guid.NewGuid()}', '2026-10-28', N'X', NULL, NULL, N'Created', N'[]');"));
        await Assert.ThrowsAsync<SqlException>(() => test.ExecuteSqlAsync(
            Insert + $"VALUES (SYSUTCDATETIME(), N'AutomatedJob', '{AccountId}', N'Order', '{Guid.NewGuid()}', '2026-10-28', N'X', NULL, NULL, N'Created', N'[]');"));
        await Assert.ThrowsAsync<SqlException>(() => test.ExecuteSqlAsync(
            Insert + $"VALUES (SYSUTCDATETIME(), N'AutomatedJob', NULL, N'Payment', '{Guid.NewGuid()}', '2026-10-28', N'X', N'Order', NULL, N'Created', N'[]');"));
    }

    private static AuditEntry Entry(AuditAuthor? author = null, string label = "ZAM/2026/0042") =>
        new(
            author ?? AuditAuthor.Account(AccountId),
            new AuditObject(AuditObjectType.Order, Guid.NewGuid(), new DateOnly(2026, 10, 28), label),
            AuditOperation.Updated,
            [new AuditChange("amount", "100.00", "120.50")]);

    /// <summary>Baza testu z migracjami wszystkich modułów i tabelą modułu testowego <c>probe.Items</c>.</summary>
    private sealed class ProbeDatabase : IAsyncDisposable
    {
        private ProbeDatabase(StoneFlowApiFactory factory, string connectionString)
        {
            Factory = factory;
            ConnectionString = connectionString;
        }

        public StoneFlowApiFactory Factory { get; }

        private string ConnectionString { get; }

        public static async Task<ProbeDatabase> CreateAsync(SqlServerFixture sqlServer)
        {
            var connectionString = sqlServer.NewDatabase("audit");
            var factory = new StoneFlowApiFactory
            {
                Time = new FakeTimeProvider(Now),
                ConfigureTestServices = services => services.AddDbContext<ProbeDbContext>((provider, options) =>
                    options.UseModuleSqlServer<ProbeDbContext>(provider, ProbeDbContext.Schema)),
            }.WithDatabase(connectionString);

            await TestDatabase.MigrateAsync(factory);
            var database = new ProbeDatabase(factory, connectionString);
            await database.ExecuteSqlAsync(
                "EXEC('CREATE SCHEMA probe'); CREATE TABLE probe.Items (Id uniqueidentifier NOT NULL PRIMARY KEY, Name nvarchar(50) NOT NULL);");
            return database;
        }

        public async Task<T> RunAsync<T>(Func<IServiceProvider, CancellationToken, Task<T>> work, Func<T, bool>? commitWhen = null)
        {
            using var scope = Factory.Services.CreateScope();
            var services = scope.ServiceProvider;
            return await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
                cancellationToken => work(services, cancellationToken),
                commitWhen ?? (_ => true),
                TestContext.Current.CancellationToken);
        }

        public async Task AddItemAsync(IServiceProvider services, CancellationToken cancellationToken, string name = "pozycja")
        {
            var probe = services.GetRequiredService<ProbeDbContext>();
            probe.Items.Add(new ProbeItem { Id = Guid.NewGuid(), Name = name });
            await probe.SaveChangesAsync(cancellationToken);
        }

        public Task<int> CountItemsAsync() => ScalarAsync<int>("SELECT COUNT(*) FROM probe.Items");

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

        public async Task<IReadOnlyList<AuditRow>> AuditRowsAsync()
        {
            await using var connection = await OpenAsync();
            await using var command = new SqlCommand(
                "SELECT OccurredAtUtc, AuthorKind, AuthorAccountId, ObjectType, ObjectId, ObjectBusinessDate, ObjectLabel, ParentObjectType, ParentObjectId, Operation, Changes FROM reporting.AuditEntries ORDER BY Id",
                connection);
            await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            var rows = new List<AuditRow>();
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                rows.Add(new AuditRow(
                    reader.GetDateTime(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    reader.GetString(3),
                    reader.GetGuid(4),
                    reader.GetDateTime(5),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetGuid(8),
                    reader.GetString(9),
                    reader.GetString(10)));
            }

            return rows;
        }

        public async ValueTask DisposeAsync() => await Factory.DisposeAsync();

        private async Task<SqlConnection> OpenAsync()
        {
            var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            return connection;
        }
    }

    private sealed record AuditRow(
        DateTime OccurredAtUtc,
        string AuthorKind,
        Guid? AuthorAccountId,
        string ObjectType,
        Guid ObjectId,
        DateTime ObjectBusinessDate,
        string ObjectLabel,
        string? ParentObjectType,
        Guid? ParentObjectId,
        string Operation,
        string Changes);
}

/// <summary>Kontekst modułu testowego (schemat <c>probe</c>), rejestrowany jak kontekst modułu finansowego.</summary>
public sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options)
{
    public const string Schema = "probe";

    public DbSet<ProbeItem> Items => Set<ProbeItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<ProbeItem>(item =>
        {
            item.ToTable("Items");
            item.HasKey(entity => entity.Id);
            item.Property(entity => entity.Name).HasMaxLength(50);
        });
    }
}

public sealed class ProbeItem
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
