using Microsoft.EntityFrameworkCore;
using StoneFlow.Reporting.Domain.Audit;

namespace StoneFlow.Reporting.Infrastructure.Persistence;

public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options) : DbContext(options)
{
    public const string Schema = "reporting";

    public DbSet<AuditRecord> AuditEntries => Set<AuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new AuditRecordConfiguration());
    }
}
