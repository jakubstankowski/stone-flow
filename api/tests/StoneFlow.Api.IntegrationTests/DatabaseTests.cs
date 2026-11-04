using System.Net;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Api.IntegrationTests.Infrastructure;

namespace StoneFlow.Api.IntegrationTests;

/// <summary>Testy wymagające SQL Server (kontener w CI albo serwer z STONEFLOW_TEST_SQLSERVER).</summary>
[Collection(SqlServerCollection.Name)]
public sealed class DatabaseTests(SqlServerFixture sqlServer)
{
    private static readonly string[] Schemas = ["access", "orders", "expenses", "reporting", "integrations"];

    [Fact]
    public async Task Health_is_healthy_when_database_available()
    {
        await using var factory = new StoneFlowApiFactory().With("ConnectionStrings:StoneFlow", sqlServer.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task All_module_migrations_apply_to_empty_database()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = sqlServer.NewDatabase("migrations");

        await using var factory = new StoneFlowApiFactory().WithDatabase(connectionString);
        await TestDatabase.MigrateAsync(factory);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        foreach (var schema in Schemas)
        {
            Assert.Equal(1, await CountTablesAsync(connection, schema, "__EFMigrationsHistory", cancellationToken));
        }

        // Moduł Access: tylko tabele konta, tokenów resetu i kluczy — bez ról, claimów, loginów i tokenów Identity.
        var accessTables = await TablesAsync(connection, "access", cancellationToken);
        Assert.Equal(["DataProtectionKeys", "PasswordResetTokens", "Users", "__EFMigrationsHistory"], accessTables);
    }

    private static async Task<int> CountTablesAsync(SqlConnection connection, string schema, string table, CancellationToken cancellationToken) =>
        (await TablesAsync(connection, schema, cancellationToken)).Count(name => name == table);

    private static async Task<string[]> TablesAsync(SqlConnection connection, string schema, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = @schema";
        command.Parameters.AddWithValue("@schema", schema);

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return [.. names.Order(StringComparer.Ordinal)];
    }
}
