using Microsoft.EntityFrameworkCore;
using StoneFlow.Expenses.Domain.Expenses;
using StoneFlow.Expenses.Domain.OwnerWithdrawals;
using StoneFlow.Expenses.Domain.Recurring;

namespace StoneFlow.Expenses.Infrastructure.Persistence;

public sealed class ExpensesDbContext(DbContextOptions<ExpensesDbContext> options) : DbContext(options)
{
    public const string Schema = "expenses";

    public DbSet<Expense> Expenses => Set<Expense>();

    public DbSet<OwnerWithdrawal> OwnerWithdrawals => Set<OwnerWithdrawal>();

    public DbSet<RecurringExpenseDefinition> RecurringDefinitions => Set<RecurringExpenseDefinition>();

    public DbSet<RecurringExpensePeriod> RecurringPeriods => Set<RecurringExpensePeriod>();

    public DbSet<RecurringDefinitionRequest> RecurringDefinitionRequests => Set<RecurringDefinitionRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ExpensesDbContext).Assembly);
    }
}
