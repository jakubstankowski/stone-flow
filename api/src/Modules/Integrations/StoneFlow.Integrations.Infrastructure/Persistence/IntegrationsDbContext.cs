using Microsoft.EntityFrameworkCore;

namespace StoneFlow.Integrations.Infrastructure.Persistence;

public sealed class IntegrationsDbContext(DbContextOptions<IntegrationsDbContext> options) : DbContext(options)
{
    public const string Schema = "integrations";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
    }
}
