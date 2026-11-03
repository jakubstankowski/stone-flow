using Microsoft.EntityFrameworkCore;

namespace StoneFlow.Expenses.Infrastructure.Persistence;

public sealed class ExpensesDbContext(DbContextOptions<ExpensesDbContext> options) : DbContext(options)
{
    public const string Schema = "expenses";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
    }
}
