using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace StoneFlow.Api.IntegrationTests.Infrastructure;

/// <summary>
/// SQL Server dla testów bazy danych, wspólny dla kolekcji. Gdy ustawiona jest zmienna
/// <see cref="ServerVariable"/> (connection string do istniejącego serwera, np. LocalDB),
/// testy używają tego serwera; w przeciwnym razie uruchamiany jest kontener (wymaga Dockera, CI).
/// Każda klasa testów dostaje własną bazę, usuwaną po zakończeniu kolekcji.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string Image = "mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04";

    public const string ServerVariable = "STONEFLOW_TEST_SQLSERVER";

    private readonly ConcurrentBag<string> _databases = [];
    private MsSqlContainer? _container;
    private string? _serverConnectionString;

    /// <summary>Connection string do serwera (baza domyślna), np. do sprawdzenia dostępności.</summary>
    public string ConnectionString =>
        _serverConnectionString ?? throw new InvalidOperationException("Serwer SQL nie został zainicjowany.");

    public async ValueTask InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable(ServerVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            _serverConnectionString = configured;
            return;
        }

        _container = new MsSqlBuilder(Image).Build();
        await _container.StartAsync();
        _serverConnectionString = _container.GetConnectionString();
    }

    /// <summary>Connection string do nowej, pustej bazy (tworzonej przez pierwszą migrację).</summary>
    public string NewDatabase(string purpose)
    {
        var name = $"StoneFlowTests_{purpose}_{Guid.NewGuid():N}";
        _databases.Add(name);
        return new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = name }.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
            return;
        }

        if (_serverConnectionString is null || _databases.IsEmpty)
        {
            return;
        }

        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        foreach (var database in _databases)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END";
            command.Parameters.AddWithValue("@name", database);
            await command.ExecuteNonQueryAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SQL Server";
}
