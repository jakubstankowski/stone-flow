using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace StoneFlow.Shared.Infrastructure.Persistence;

/// <summary>
/// Wspólna konfiguracja providera EF Core dla modułów: SQL Server, historia migracji
/// w schemacie modułu i migracje w zestawie infrastruktury modułu.
/// </summary>
public static class ModuleDbContextOptions
{
    public const string ConnectionStringName = "StoneFlow";

    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    public static DbContextOptionsBuilder UseModuleSqlServer<TContext>(
        this DbContextOptionsBuilder builder,
        string? connectionString,
        string schema)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        return builder.UseSqlServer(connectionString, sql => sql
            .MigrationsHistoryTable(MigrationsHistoryTable, schema)
            .MigrationsAssembly(typeof(TContext).Assembly.GetName().Name));
    }

    /// <summary>
    /// Kontekst modułu finansowego: wspólne połączenie zakresu (<see cref="ModuleConnection"/>) i
    /// udział w transakcji jednostki pracy, w tym zapisu audytu.
    /// </summary>
    public static DbContextOptionsBuilder UseModuleSqlServer<TContext>(
        this DbContextOptionsBuilder builder,
        IServiceProvider serviceProvider,
        string schema)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        var connection = serviceProvider.GetRequiredService<ModuleConnection>();
        return builder
            .UseSqlServer(connection.Connection, sql => sql
                .MigrationsHistoryTable(MigrationsHistoryTable, schema)
                .MigrationsAssembly(typeof(TContext).Assembly.GetName().Name))
            .AddInterceptors(new ModuleTransactionInterceptor(connection));
    }
}
