using System.Net;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Expenses.Infrastructure.Persistence;
using StoneFlow.Integrations.Infrastructure.Persistence;
using StoneFlow.Orders.Infrastructure.Persistence;
using StoneFlow.Reporting.Infrastructure.Persistence;

namespace StoneFlow.Api.IntegrationTests;

/// <summary>Testy wymagające SQL Server (kontener w CI albo serwer z STONEFLOW_TEST_SQLSERVER).</summary>
[Collection(SqlServerCollection.Name)]
public sealed class DatabaseTests(SqlServerFixture sqlServer)
{
    private static readonly string[] Schemas = ["orders", "expenses", "reporting", "integrations"];

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

        await using var factory = new StoneFlowApiFactory().With("ConnectionStrings:StoneFlow", connectionString);
        using (var scope = factory.Services.CreateScope())
        {
            DbContext[] contexts =
            [
                scope.ServiceProvider.GetRequiredService<OrdersDbContext>(),
                scope.ServiceProvider.GetRequiredService<ExpensesDbContext>(),
                scope.ServiceProvider.GetRequiredService<ReportingDbContext>(),
                scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>(),
            ];

            foreach (var context in contexts)
            {
                await context.Database.MigrateAsync(cancellationToken);
            }
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        foreach (var schema in Schemas)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id " +
                "WHERE s.name = @schema AND t.name = '__EFMigrationsHistory'";
            command.Parameters.AddWithValue("@schema", schema);

            var historyTables = (int)(await command.ExecuteScalarAsync(cancellationToken))!;
            Assert.Equal(1, historyTables);
        }
    }
}
