using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StoneFlow.Orders.Infrastructure.Persistence;

/// <summary>Licznik numerów zamówień w roku (encja techniczna numeracji).</summary>
internal sealed class OrderNumberCounter
{
    public int Year { get; set; }

    public int LastNumber { get; set; }
}

internal sealed class OrderNumberCounterConfiguration : IEntityTypeConfiguration<OrderNumberCounter>
{
    public const string Table = "OrderNumberCounters";

    public void Configure(EntityTypeBuilder<OrderNumberCounter> builder)
    {
        builder.ToTable(Table);
        builder.HasKey(counter => counter.Year);
        builder.Property(counter => counter.Year).ValueGeneratedNever();
    }
}
