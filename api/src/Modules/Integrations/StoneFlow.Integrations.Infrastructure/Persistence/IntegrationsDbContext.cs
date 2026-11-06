using Microsoft.EntityFrameworkCore;
using StoneFlow.Integrations.Domain.WooCommerce;

namespace StoneFlow.Integrations.Infrastructure.Persistence;

/// <summary>
/// Schemat <c>integrations</c>: kursor, przebiegi i historia błędów synchronizacji WooCommerce (SF-13).
/// Kontekst ma własne połączenie (nie <c>ModuleConnection</c>), jak harmonogram SF-11: zapisy historii
/// i przebiegów używają transakcji własnego kontekstu i nie dołączają do jednostki pracy zamówień.
/// </summary>
public sealed class IntegrationsDbContext(DbContextOptions<IntegrationsDbContext> options) : DbContext(options)
{
    public const string Schema = "integrations";

    public const string OrderErrorsTable = "WooCommerceOrderErrors";

    public DbSet<WooCommerceSyncState> SyncStates => Set<WooCommerceSyncState>();

    public DbSet<WooCommerceSyncRun> SyncRuns => Set<WooCommerceSyncRun>();

    public DbSet<WooCommerceOrderError> OrderErrors => Set<WooCommerceOrderError>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<WooCommerceSyncState>(state =>
        {
            state.ToTable("WooCommerceSyncState");
            state.HasKey(s => s.Id);
            state.Property(s => s.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<WooCommerceSyncRun>(run =>
        {
            run.ToTable("WooCommerceSyncRuns");
            run.HasKey(r => r.Id);
            run.Property(r => r.Id).ValueGeneratedNever();
            run.Property(r => r.Source).HasMaxLength(16);
            run.Property(r => r.Status).HasMaxLength(16);
            run.Property(r => r.Message).HasMaxLength(OrderErrorRules.MaxMessageLength);
            run.HasIndex(r => r.StartedAtUtc).IsDescending();
        });

        modelBuilder.Entity<WooCommerceOrderError>(error =>
        {
            error.ToTable(OrderErrorsTable);
            error.HasKey(e => e.Id);
            error.Property(e => e.Id).ValueGeneratedNever();
            error.Property(e => e.ExternalId).HasMaxLength(OrderErrorRules.MaxExternalIdLength);
            error.Property(e => e.Status).HasMaxLength(16);
            error.Property(e => e.Source).HasMaxLength(16);
            error.Property(e => e.ClosedBy).HasMaxLength(16);
            error.Property(e => e.Message).HasMaxLength(OrderErrorRules.MaxMessageLength);
            error.Property(e => e.Context).HasMaxLength(OrderErrorRules.MaxContextLength);
            error.HasIndex(e => e.ExternalId).IsUnique().HasFilter("[Status] = N'open'");
            error.HasIndex(e => e.LastOccurredAtUtc).IsDescending();
        });
    }
}

/// <summary>Jedyny wiersz stanu synchronizacji (<c>Id</c> = 1); tworzony przy pierwszym sukcesie.</summary>
public sealed class WooCommerceSyncState
{
    public const int SingletonId = 1;

    public int Id { get; init; }

    /// <summary>Górna granica okna ostatniego udanego przebiegu (UTC).</summary>
    public DateTime? LastSuccessCursorUtc { get; set; }
}

/// <summary>Przebieg uzupełniania; czasy UTC (<c>datetime2</c>).</summary>
public sealed class WooCommerceSyncRun
{
    public Guid Id { get; init; }

    public required string Source { get; init; }

    public DateTime StartedAtUtc { get; init; }

    public DateTime? FinishedAtUtc { get; set; }

    public required string Status { get; set; }

    public int OrdersProcessed { get; set; }

    public int Errors { get; set; }

    public int Unidentified { get; set; }

    public string? Message { get; set; }
}

/// <summary>Błąd zamówienia w historii; najwyżej jeden otwarty na <c>ExternalId</c>.</summary>
public sealed class WooCommerceOrderError
{
    public Guid Id { get; init; }

    public required string ExternalId { get; init; }

    public required string Status { get; set; }

    public required string Source { get; init; }

    public DateTime FirstOccurredAtUtc { get; init; }

    public DateTime LastOccurredAtUtc { get; set; }

    public int Attempts { get; set; }

    public required string Message { get; set; }

    public required string Context { get; set; }

    public DateTime? FailedModifiedAtUtc { get; set; }

    public DateTime? ClosedAtUtc { get; set; }

    public string? ClosedBy { get; set; }
}
