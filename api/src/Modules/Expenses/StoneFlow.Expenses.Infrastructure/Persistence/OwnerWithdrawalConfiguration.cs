using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoneFlow.Expenses.Domain.OwnerWithdrawals;

namespace StoneFlow.Expenses.Infrastructure.Persistence;

/// <summary>
/// Tabela <c>expenses.OwnerWithdrawals</c>: kwota <c>decimal(18,2)</c> &gt; 0 i metoda ze słownika
/// powtórzone w <c>CHECK</c>, unikalny klucz idempotencji, indeks po dacie wypłaty pod listę
/// operacji pieniężnych i <c>rowversion</c>.
/// </summary>
internal sealed class OwnerWithdrawalConfiguration : IEntityTypeConfiguration<OwnerWithdrawal>
{
    public const string Table = "OwnerWithdrawals";

    /// <summary>Token współbieżności (<c>rowversion</c>), właściwość cienia — domena nie zna EF.</summary>
    public const string VersionProperty = "Version";

    public const string AmountConstraint = "CK_OwnerWithdrawals_Amount";
    public const string MethodConstraint = "CK_OwnerWithdrawals_Method";
    public const string CreationRequestConstraint = "CK_OwnerWithdrawals_CreationRequest";
    public const string CreationRequestIndex = "UX_OwnerWithdrawals_CreationRequestId";
    public const string WithdrawnOnIndex = "IX_OwnerWithdrawals_WithdrawnOn";

    private const int MethodMaxLength = 16;

    public void Configure(EntityTypeBuilder<OwnerWithdrawal> builder)
    {
        builder.ToTable(Table, table =>
        {
            table.HasCheckConstraint(AmountConstraint, "[Amount] > 0");
            table.HasCheckConstraint(MethodConstraint, "[Method] IN (N'Transfer', N'Blik', N'Cash')");
            table.HasCheckConstraint(
                CreationRequestConstraint,
                "([CreationRequestId] IS NULL AND [CreationRequestHash] IS NULL) OR ([CreationRequestId] IS NOT NULL AND [CreationRequestHash] IS NOT NULL)");
        });

        builder.HasKey(withdrawal => withdrawal.Id);
        builder.Property(withdrawal => withdrawal.Id).ValueGeneratedNever();
        builder.Property(withdrawal => withdrawal.Person).HasMaxLength(OwnerWithdrawal.PersonMaxLength).IsRequired();
        builder.Property(withdrawal => withdrawal.Amount).HasPrecision(18, 2);
        builder.Property(withdrawal => withdrawal.WithdrawnOn).HasColumnType("date");
        builder.Property(withdrawal => withdrawal.Method).HasConversion<string>().HasMaxLength(MethodMaxLength);
        builder.Property(withdrawal => withdrawal.CreationRequestHash).HasColumnType("binary(32)");
        builder.Property(withdrawal => withdrawal.CreatedAtUtc).HasColumnType("datetime2");
        builder.Property(withdrawal => withdrawal.UpdatedAtUtc).HasColumnType("datetime2");
        builder.Property<byte[]>(VersionProperty).IsRowVersion().IsRequired();

        builder.HasIndex(withdrawal => withdrawal.CreationRequestId)
            .IsUnique()
            .HasFilter("[CreationRequestId] IS NOT NULL")
            .HasDatabaseName(CreationRequestIndex);
        builder.HasIndex(withdrawal => withdrawal.WithdrawnOn)
            .IncludeProperties(withdrawal => withdrawal.Amount)
            .HasDatabaseName(WithdrawnOnIndex);
    }
}
