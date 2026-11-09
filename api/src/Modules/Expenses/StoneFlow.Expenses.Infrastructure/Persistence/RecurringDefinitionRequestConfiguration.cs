using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoneFlow.Expenses.Domain.Recurring;

namespace StoneFlow.Expenses.Infrastructure.Persistence;

/// <summary>Klucze idempotencji dodania <c>expenses.RecurringDefinitionRequests</c>; bez klucza obcego — zostają po usunięciu definicji.</summary>
internal sealed class RecurringDefinitionRequestConfiguration : IEntityTypeConfiguration<RecurringDefinitionRequest>
{
    public const string Table = "RecurringDefinitionRequests";

    public void Configure(EntityTypeBuilder<RecurringDefinitionRequest> builder)
    {
        builder.ToTable(Table, table => table.HasCheckConstraint("CK_RecurringDefinitionRequests_CreatedOccurrences", "[CreatedOccurrences] >= 0"));
        builder.HasKey(request => request.Key);
        builder.Property(request => request.Key).ValueGeneratedNever();
        builder.Property(request => request.RequestHash).HasColumnType("binary(32)").IsRequired();
        builder.Property(request => request.CreatedAtUtc).HasColumnType("datetime2");
        builder.HasIndex(request => request.DefinitionId).HasDatabaseName("IX_RecurringDefinitionRequests_DefinitionId");
    }
}
