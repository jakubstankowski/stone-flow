using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Orders.Domain.Payments;

namespace StoneFlow.Orders.Infrastructure.Persistence;

/// <summary>
/// Wspólne mapowanie wpisów (wpłat, zwrotów i korekt sprzedaży; osobne tabele): kwota <c>decimal(18,2)</c> &gt; 0,
/// FK do zamówienia bez kaskady (zamówień się nie usuwa), indeks <c>(OrderId) INCLUDE (Amount)</c>
/// pod sumy, unikalny klucz idempotencji i <c>rowversion</c>.
/// </summary>
internal static class OrderEntryConfiguration
{
    /// <summary>Token współbieżności (<c>rowversion</c>), właściwość cienia — domena nie zna EF.</summary>
    public const string VersionProperty = "Version";

    public const int CodeMaxLength = 16;

    public static void Configure<TEntry>(EntityTypeBuilder<TEntry> builder, string table, string dateColumn, Action<TableBuilder<TEntry>> constraints)
        where TEntry : OrderEntry
    {
        builder.ToTable(table, t =>
        {
            t.HasCheckConstraint($"CK_{table}_Amount", "[Amount] > 0");
            constraints(t);
        });

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Amount).HasPrecision(18, 2);
        builder.Property(entry => entry.Date).HasColumnName(dateColumn).HasColumnType("date");
        builder.Property(entry => entry.CreationRequestHash).HasColumnType("binary(32)");
        builder.Property(entry => entry.CreatedAtUtc).HasColumnType("datetime2");
        builder.Property(entry => entry.UpdatedAtUtc).HasColumnType("datetime2");
        builder.Property<byte[]>(VersionProperty).IsRowVersion().IsRequired();

        builder.HasOne<Order>().WithMany().HasForeignKey(entry => entry.OrderId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(entry => entry.OrderId).IncludeProperties(entry => entry.Amount);
        builder.HasIndex(entry => entry.CreationRequestId).IsUnique().HasFilter("[CreationRequestId] IS NOT NULL");
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public const string Table = "Payments";

    public const string ReceivedByConstraint = "CK_Payments_ReceivedBy";

    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        OrderEntryConfiguration.Configure(
            builder,
            Table,
            "PaidOn",
            table => table.HasCheckConstraint(ReceivedByConstraint, "[ReceivedBy] IS NULL OR [Method] = N'Cash'"));

        builder.Property(payment => payment.Method).HasConversion<string>().HasMaxLength(OrderEntryConfiguration.CodeMaxLength);
        builder.Property(payment => payment.ReceivedBy).HasMaxLength(Payment.ReceivedByMaxLength);
    }
}

internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public const string Table = "Refunds";

    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        OrderEntryConfiguration.Configure(builder, Table, "RefundedOn", _ => { });

        builder.Property(refund => refund.Kind).HasConversion<string>().HasMaxLength(OrderEntryConfiguration.CodeMaxLength);
        builder.Property(refund => refund.Method).HasConversion<string>().HasMaxLength(OrderEntryConfiguration.CodeMaxLength);
        builder.Property(refund => refund.Reason).HasMaxLength(Refund.ReasonMaxLength).IsRequired();
    }
}

internal sealed class SalesCorrectionConfiguration : IEntityTypeConfiguration<SalesCorrection>
{
    public const string Table = "SalesCorrections";

    public void Configure(EntityTypeBuilder<SalesCorrection> builder)
    {
        OrderEntryConfiguration.Configure(builder, Table, "CorrectedOn", _ => { });

        builder.Property(correction => correction.Reason).HasMaxLength(SalesCorrection.ReasonMaxLength).IsRequired();
    }
}
