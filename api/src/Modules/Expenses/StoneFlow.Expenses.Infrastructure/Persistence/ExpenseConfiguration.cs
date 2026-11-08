using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoneFlow.Expenses.Domain.Expenses;

namespace StoneFlow.Expenses.Infrastructure.Persistence;

/// <summary>
/// Tabela <c>expenses.Expenses</c>: kwoty <c>decimal(18,2)</c>, reguły kwot powtórzone w <c>CHECK</c>
/// (brutto ≠ 0; netto 0 albo tego samego znaku, |netto| ≤ |brutto|), unikalny klucz idempotencji
/// (filtrowany — wydatki z SF-16 nie mają klucza), powiązanie wystąpienia z definicją (obie kolumny
/// albo żadna), indeksy pod filtry dat i <c>rowversion</c>.
/// </summary>
internal sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public const string Table = "Expenses";

    /// <summary>Token współbieżności (<c>rowversion</c>), właściwość cienia — domena nie zna EF.</summary>
    public const string VersionProperty = "Version";

    public const string GrossAmountConstraint = "CK_Expenses_GrossAmount";
    public const string NetAmountConstraint = "CK_Expenses_NetAmount";
    public const string CreationRequestConstraint = "CK_Expenses_CreationRequest";
    public const string CreationRequestIndex = "UX_Expenses_CreationRequestId";
    public const string RecurringConstraint = "CK_Expenses_Recurring";

    private const int CategoryMaxLength = 32;
    private const int MethodMaxLength = 16;

    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable(Table, table =>
        {
            table.HasCheckConstraint(GrossAmountConstraint, "[GrossAmount] <> 0");
            table.HasCheckConstraint(
                NetAmountConstraint,
                "[NetAmount] IS NULL OR (ABS([NetAmount]) <= ABS([GrossAmount]) AND ([NetAmount] = 0 OR SIGN([NetAmount]) = SIGN([GrossAmount])))");
            table.HasCheckConstraint(
                CreationRequestConstraint,
                "([CreationRequestId] IS NULL AND [CreationRequestHash] IS NULL) OR ([CreationRequestId] IS NOT NULL AND [CreationRequestHash] IS NOT NULL)");
            table.HasCheckConstraint(
                RecurringConstraint,
                "([RecurringDefinitionId] IS NULL AND [RecurringPeriod] IS NULL) OR ([RecurringDefinitionId] IS NOT NULL AND [RecurringPeriod] IS NOT NULL)");
        });

        builder.HasKey(expense => expense.Id);
        builder.Property(expense => expense.Id).ValueGeneratedNever();
        builder.Property(expense => expense.Description).HasMaxLength(Expense.DescriptionMaxLength).IsRequired();
        builder.Property(expense => expense.Category).HasConversion<string>().HasMaxLength(CategoryMaxLength);
        builder.Property(expense => expense.Counterparty).HasMaxLength(Expense.CounterpartyMaxLength);
        builder.Property(expense => expense.CostDate).HasColumnType("date");
        builder.Property(expense => expense.DueDate).HasColumnType("date");
        builder.Property(expense => expense.PaidOn).HasColumnType("date");
        builder.Property(expense => expense.GrossAmount).HasPrecision(18, 2);
        builder.Property(expense => expense.NetAmount).HasPrecision(18, 2);
        builder.Property(expense => expense.DocumentReference).HasMaxLength(Expense.DocumentReferenceMaxLength);
        builder.Property(expense => expense.PaymentMethod).HasConversion<string>().HasMaxLength(MethodMaxLength);
        builder.Property(expense => expense.Payer).HasMaxLength(Expense.PayerMaxLength);
        builder.Property(expense => expense.Comment).HasMaxLength(Expense.CommentMaxLength);
        builder.Property(expense => expense.CreationRequestHash).HasColumnType("binary(32)");
        builder.Property(expense => expense.RecurringPeriod).HasColumnType("date");
        builder.Property(expense => expense.CreatedAtUtc).HasColumnType("datetime2");
        builder.Property(expense => expense.UpdatedAtUtc).HasColumnType("datetime2");
        builder.Property<byte[]>(VersionProperty).IsRowVersion().IsRequired();

        builder.HasIndex(expense => expense.CreationRequestId)
            .IsUnique()
            .HasFilter("[CreationRequestId] IS NOT NULL")
            .HasDatabaseName(CreationRequestIndex);
        builder.HasIndex(expense => new { expense.CostDate, expense.Id }).HasDatabaseName("IX_Expenses_CostDate");
        builder.HasIndex(expense => new { expense.PaidOn, expense.Id })
            .HasFilter("[PaidOn] IS NOT NULL")
            .HasDatabaseName("IX_Expenses_PaidOn");

        // Wystąpienia jednej definicji (filtr listy, liczba wystąpień); bez klucza obcego — definicję można usunąć.
        builder.HasIndex(expense => new { expense.RecurringDefinitionId, expense.CostDate, expense.Id })
            .HasFilter("[RecurringDefinitionId] IS NOT NULL")
            .HasDatabaseName("IX_Expenses_RecurringDefinitionId");
    }
}
