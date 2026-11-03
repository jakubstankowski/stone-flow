using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StoneFlow.Shared.Infrastructure.Persistence;

namespace StoneFlow.Api.Health;

/// <summary>Sprawdza połączenie z SQL Server. Wynik nie zawiera szczegółów błędu.</summary>
internal sealed class SqlServerHealthCheck(IConfiguration configuration, ILogger<SqlServerHealthCheck> logger) : IHealthCheck
{
    private const int TimeoutSeconds = 5;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString(ModuleDbContextOptions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning("Health check SQL: brak connection stringa {Name}.", ModuleDbContextOptions.ConnectionStringName);
            return HealthCheckResult.Unhealthy();
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString) { ConnectTimeout = TimeoutSeconds };
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = TimeoutSeconds;
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException or ArgumentException)
        {
            logger.LogWarning("Health check SQL nie powiódł się: {ErrorType}.", exception.GetType().Name);
            return HealthCheckResult.Unhealthy();
        }
    }
}
