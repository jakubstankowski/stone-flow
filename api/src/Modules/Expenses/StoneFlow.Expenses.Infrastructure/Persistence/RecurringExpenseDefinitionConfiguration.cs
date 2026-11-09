using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoneFlow.Expenses.Domain.Expenses;
using StoneFlow.Expenses.Domain.Recurring;

namespace StoneFlow.Expenses.Infrastructure.Persistence;

/// <summary>
/// Tabela <c>expenses.RecurringExpenseDefinitions</c> z częściami definicji: historią dni
/// (<c>RecurringExpenseDays</c>) i przedziałami wyłączeń (<c>RecurringExpenseExclusions</c>), obie
/// z kluczem obcym <c>ON DELETE CASCADE</c>. Reguły kwot, dnia i zakresu powtórzone w <c>CHECK</c>;
/// <c>LastDueDate</c> ma jawne oba człony, bo <c>CHECK</c> przepuszcza wynik <c>UNKNOWN</c>.
/// </summary>
internal sealed class RecurringExpenseDefinitionConfiguration : IEntityTypeConfiguration<RecurringExpenseDefinition>
{
    public const string Table = "RecurringExpenseDefinitions";
    public const string DaysTable = "RecurringExpenseDays";
    public const string ExclusionsTable = "RecurringExpenseExclusions";
    public const string DefinitionIdColumn = "DefinitionId";

    /// <summary>Token współbieżności (<c>rowversion</c>), właściwość cienia — domena nie zna EF.</summary>
    public const string VersionProperty = "Version";

    private const int CategoryMaxLength = 32;
    private const int MethodMaxLength = 16;
    private const int ReasonMaxLength = 16;

    public void Configure(EntityTypeBuilder<RecurringExpenseDefinition> builder)
    {
        builder.ToTable(Table, table =>
        {
            table.HasCheckConstraint("CK_RecurringExpenseDefinitions_GrossAmount", "[GrossAmount] > 0");
            table.HasCheckConstraint(
                "CK_RecurringExpenseDefinitions_NetAmount",
                "[NetAmount] IS NULL OR ([NetAmount] >= 0 AND [NetAmount] <= [GrossAmount])");
            table.HasCheckConstraint("CK_RecurringExpenseDefinitions_DayOfMonth", "[DayOfMonth] BETWEEN 1 AND 31");
            table.HasCheckConstraint("CK_RecurringExpenseDefinitions_EndsOn", "[EndsOn] IS NULL OR [EndsOn] >= [StartsOn]");
            table.HasCheckConstraint(
                "CK_RecurringExpenseDefinitions_LastDueDate",
                "([EndsOn] IS NULL AND [LastDueDate] IS NULL) OR ([EndsOn] IS NOT NULL AND [LastDueDate] IS NOT NULL AND [LastDueDate] >= [StartsOn] AND [LastDueDate] <= [EndsOn])");
        });

        builder.HasKey(definition => definition.Id);
        builder.Property(definition => definition.Id).ValueGeneratedNever();
        builder.Property(definition => definition.Description).HasMaxLength(Expense.DescriptionMaxLength).IsRequired();
        builder.Property(definition => definition.Category).HasConversion<string>().HasMaxLength(CategoryMaxLength);
        builder.Property(definition => definition.Counterparty).HasMaxLength(Expense.CounterpartyMaxLength);
        builder.Property(definition => definition.GrossAmount).HasPrecision(18, 2);
        builder.Property(definition => definition.NetAmount).HasPrecision(18, 2);
        builder.Property(definition => definition.PaymentMethod).HasConversion<string>().HasMaxLength(MethodMaxLength);
        builder.Property(definition => definition.Payer).HasMaxLength(Expense.PayerMaxLength);
        builder.Property(definition => definition.Comment).HasMaxLength(Expense.CommentMaxLength);
        builder.Property(definition => definition.DayOfMonth).HasConversion<byte>().HasColumnType("tinyint");
        builder.Property(definition => definition.StartsOn).HasColumnType("date");
        builder.Property(definition => definition.EndsOn).HasColumnType("date");
        builder.Property(definition => definition.LastDueDate).HasColumnType("date");
        builder.Property(definition => definition.PausedOn).HasColumnType("date");
        builder.Property(definition => definition.CreatedAtUtc).HasColumnType("datetime2");
        builder.Property(definition => definition.UpdatedAtUtc).HasColumnType("datetime2");
        builder.Property<byte[]>(VersionProperty).IsRowVersion().IsRequired();
        builder.Ignore(definition => definition.Schedule);

        builder.HasIndex(definition => new { definition.Description, definition.Id }).HasDatabaseName("IX_RecurringExpenseDefinitions_Description");
        builder.HasIndex(definition => new { definition.PausedOn, definition.EndsOn })
            .IncludeProperties(definition => definition.StartsOn)
            .HasDatabaseName("IX_RecurringExpenseDefinitions_Generation");

        builder.OwnsMany(definition => definition.Days, days =>
        {
            days.ToTable(DaysTable, table =>
            {
                table.HasCheckConstraint("CK_RecurringExpenseDays_FromPeriod", "DAY([FromPeriod]) = 1");
                table.HasCheckConstraint("CK_RecurringExpenseDays_DayOfMonth", "[DayOfMonth] BETWEEN 1 AND 31");
            });
            days.WithOwner().HasForeignKey(DefinitionIdColumn);
            days.HasKey(DefinitionIdColumn, nameof(RecurringExpenseDay.FromPeriod));
            days.Property(day => day.FromPeriod).HasColumnType("date");
            days.Property(day => day.DayOfMonth).HasConversion<byte>().HasColumnType("tinyint");
        });
        builder.Navigation(definition => definition.Days).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(definition => definition.Exclusions, exclusions =>
        {
            exclusions.ToTable(ExclusionsTable, table =>
            {
                table.HasCheckConstraint("CK_RecurringExpenseExclusions_Range", "[ToDate] > [FromDate]");
                table.HasCheckConstraint("CK_RecurringExpenseExclusions_Reason", "[Reason] IN ('Pause', 'StartMoved')");
            });
            exclusions.WithOwner().HasForeignKey(DefinitionIdColumn);
            exclusions.HasKey(exclusion => exclusion.Id);
            exclusions.Property(exclusion => exclusion.Id).ValueGeneratedNever();
            exclusions.Property(exclusion => exclusion.FromDate).HasColumnType("date");
            exclusions.Property(exclusion => exclusion.ToDate).HasColumnType("date");
            exclusions.Property(exclusion => exclusion.Reason).HasConversion<string>().HasMaxLength(ReasonMaxLength);
            exclusions.Property(exclusion => exclusion.CreatedAtUtc).HasColumnType("datetime2");
            exclusions.HasIndex(DefinitionIdColumn).HasDatabaseName("IX_RecurringExpenseExclusions_DefinitionId");
        });
        builder.Navigation(definition => definition.Exclusions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
