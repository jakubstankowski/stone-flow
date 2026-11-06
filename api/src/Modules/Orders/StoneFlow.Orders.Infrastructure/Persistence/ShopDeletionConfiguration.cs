using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoneFlow.Orders.Domain.Orders;

namespace StoneFlow.Orders.Infrastructure.Persistence;

internal sealed class ShopDeletionConfiguration : IEntityTypeConfiguration<ShopDeletion>
{
    public void Configure(EntityTypeBuilder<ShopDeletion> builder)
    {
        builder.ToTable("ShopDeletions");
        builder.HasKey(deletion => deletion.ExternalId);
        builder.Property(deletion => deletion.ExternalId).HasMaxLength(Order.ExternalIdMaxLength);
        builder.Property(deletion => deletion.ReceivedAtUtc).HasColumnType("datetime2");
    }
}
