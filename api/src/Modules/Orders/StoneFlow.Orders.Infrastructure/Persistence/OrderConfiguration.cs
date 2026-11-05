using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using StoneFlow.Orders.Domain.Orders;

namespace StoneFlow.Orders.Infrastructure.Persistence;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public const string Table = "Orders";

    /// <summary>Token współbieżności (<c>rowversion</c>), właściwość cienia — domena nie zna EF.</summary>
    public const string VersionProperty = "Version";

    public const string SourceConstraint = "CK_Orders_Source_ExternalId";

    public const string ValueConstraint = "CK_Orders_Value";

    private const int CodeMaxLength = 16;

    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable(Table, table =>
        {
            table.HasCheckConstraint(
                SourceConstraint,
                "([Source] = N'Manual' AND [ExternalId] IS NULL) OR ([Source] <> N'Manual' AND [ExternalId] IS NOT NULL)");
            table.HasCheckConstraint(ValueConstraint, "[Value] >= 0");
        });

        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).ValueGeneratedNever();
        builder.Property(order => order.Number).HasMaxLength(Order.NumberMaxLength).IsRequired();
        builder.Property(order => order.Source).HasConversion<string>().HasMaxLength(CodeMaxLength);
        builder.Property(order => order.ExternalId).HasMaxLength(Order.ExternalIdMaxLength);
        builder.Property(order => order.Channel).HasConversion<string>().HasMaxLength(CodeMaxLength);
        builder.Property(order => order.OrderDate).HasColumnType("date");
        builder.Property(order => order.Value).HasPrecision(18, 2);
        builder.Property(order => order.FulfillmentStatus).HasConversion<string>().HasMaxLength(CodeMaxLength);
        builder.Property(order => order.ShopStatus).HasMaxLength(Order.ShopStatusMaxLength);

        builder.ComplexProperty(order => order.Customer, customer =>
        {
            customer.Property(c => c.Kind).HasColumnName("CustomerKind").HasConversion<string>().HasMaxLength(CodeMaxLength);
            customer.Property(c => c.Name).HasColumnName("CustomerName").HasMaxLength(Customer.NameMaxLength).IsRequired();
            customer.Property(c => c.Phone).HasColumnName("CustomerPhone").HasMaxLength(Customer.PhoneMaxLength);
            customer.Property(c => c.Email).HasColumnName("CustomerEmail").HasMaxLength(Customer.EmailMaxLength);
            customer.Property(c => c.Street).HasColumnName("CustomerStreet").HasMaxLength(Customer.StreetMaxLength);
            customer.Property(c => c.PostalCode).HasColumnName("CustomerPostalCode").HasMaxLength(Customer.PostalCodeMaxLength);
            customer.Property(c => c.City).HasColumnName("CustomerCity").HasMaxLength(Customer.CityMaxLength);
            customer.Property(c => c.TaxId).HasColumnName("CustomerTaxId").HasMaxLength(Customer.TaxIdMaxLength);
        });

        builder.ComplexProperty(order => order.Operational, operational =>
        {
            operational.Property(o => o.Suppliers)
                .HasColumnName("Suppliers")
                .HasConversion(TextListJson.Converter, TextListJson.Comparer)
                .HasMaxLength(4000)
                .IsRequired();
            operational.Property(o => o.ZoReferences)
                .HasColumnName("ZoReferences")
                .HasConversion(TextListJson.Converter, TextListJson.Comparer)
                .HasMaxLength(2000)
                .IsRequired();
            operational.Property(o => o.InvoiceNumber).HasColumnName("InvoiceNumber").HasMaxLength(OperationalData.InvoiceNumberMaxLength);
            operational.Property(o => o.HasPhotos).HasColumnName("HasPhotos");
            operational.Property(o => o.HasReview).HasColumnName("HasReview");
            operational.Property(o => o.Notes).HasColumnName("Notes").HasMaxLength(OperationalData.NotesMaxLength);
        });

        builder.Property(order => order.Comment).HasMaxLength(Order.CommentMaxLength);
        builder.Property(order => order.CreationRequestHash).HasColumnType("binary(32)");
        builder.Property(order => order.CreatedAtUtc).HasColumnType("datetime2");
        builder.Property(order => order.UpdatedAtUtc).HasColumnType("datetime2");
        builder.Property<byte[]>(VersionProperty).IsRowVersion().IsRequired();

        builder.Ignore(order => order.IsCancelled);
        builder.Ignore(order => order.ShopOwnedFields);

        builder.HasIndex(order => order.Number).IsUnique();
        builder.HasIndex(order => new { order.Source, order.ExternalId }).IsUnique().HasFilter("[ExternalId] IS NOT NULL");
        builder.HasIndex(order => order.CreationRequestId).IsUnique().HasFilter("[CreationRequestId] IS NOT NULL");
        builder.HasIndex(order => new { order.OrderDate, order.Id }).IsDescending(true, true);
        builder.HasIndex(order => order.Channel);
        builder.HasIndex(order => order.FulfillmentStatus);
    }
}

/// <summary>Lista krótkich tekstów jako tablica JSON w kolumnie (znaki spoza ASCII bez ucieczek, jak w SF-7).</summary>
internal static class TextListJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static readonly ValueConverter<IReadOnlyList<string>, string> Converter = new(
        items => JsonSerializer.Serialize(items, Options),
        json => JsonSerializer.Deserialize<string[]>(json, Options) ?? Array.Empty<string>());

    public static readonly ValueComparer<IReadOnlyList<string>> Comparer = new(
        (left, right) => ReferenceEquals(left, right) || (left != null && right != null && left.SequenceEqual(right)),
        items => items.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
        items => items.ToArray());
}
