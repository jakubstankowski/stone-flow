using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoneFlow.Expenses.Domain.Recurring;

namespace StoneFlow.Expenses.Infrastructure.Persistence;

/// <summary>
/// Rejestr okresów <c>expenses.RecurringExpensePeriods</c>: <c>PK_RecurringExpensePeriods</c>
/// (definicja, okres) to niezmiennik 4; wystąpienie bez klucza obcego (wydatek można usunąć), definicja
/// z kaskadą.
/// </summary>
internal sealed class RecurringExpensePeriodConfiguration : IEntityTypeConfiguration<RecurringExpensePeriod>
{
    public const string Table = "RecurringExpensePeriods";
    public const string PrimaryKey = "PK_RecurringExpensePeriods";

    public void Configure(EntityTypeBuilder<RecurringExpensePeriod> builder)
    {
        builder.ToTable(Table, table => table.HasCheckConstraint("CK_RecurringExpensePeriods_Period", "DAY([Period]) = 1"));
        builder.HasKey(period => new { period.DefinitionId, period.Period }).HasName(PrimaryKey);
        builder.Property(period => period.Period).HasColumnType("date");
        builder.Property(period => period.DueDate).HasColumnType("date");
        builder.Property(period => period.CreatedAtUtc).HasColumnType("datetime2");
        builder.HasIndex(period => period.ExpenseId).IsUnique().HasDatabaseName("UX_RecurringExpensePeriods_ExpenseId");
        builder.HasOne<RecurringExpenseDefinition>()
            .WithMany()
            .HasForeignKey(period => period.DefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
