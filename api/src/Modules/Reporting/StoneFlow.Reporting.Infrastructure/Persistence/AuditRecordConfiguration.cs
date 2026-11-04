using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using StoneFlow.Reporting.Domain.Audit;

namespace StoneFlow.Reporting.Infrastructure.Persistence;

internal sealed class AuditRecordConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    public const string Table = "AuditEntries";

    public const string AuthorConstraint = "CK_AuditEntries_Author";

    public const string ParentConstraint = "CK_AuditEntries_Parent";

    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.ToTable(Table, table =>
        {
            table.HasCheckConstraint(
                AuthorConstraint,
                "([AuthorKind] = N'Account' AND [AuthorAccountId] IS NOT NULL) OR ([AuthorKind] <> N'Account' AND [AuthorAccountId] IS NULL)");
            table.HasCheckConstraint(
                ParentConstraint,
                "([ParentObjectType] IS NULL AND [ParentObjectId] IS NULL) OR ([ParentObjectType] IS NOT NULL AND [ParentObjectId] IS NOT NULL)");
        });

        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).UseIdentityColumn();
        builder.Property(record => record.OccurredAtUtc).HasColumnType("datetime2");
        builder.Property(record => record.AuthorKind).HasConversion<string>().HasMaxLength(30);
        builder.Property(record => record.ObjectType).HasConversion<string>().HasMaxLength(50);
        builder.Property(record => record.ObjectBusinessDate).HasColumnType("date");
        builder.Property(record => record.ObjectLabel).HasMaxLength(AuditRecord.LabelMaxLength);
        builder.Property(record => record.ParentObjectType).HasConversion<string>().HasMaxLength(50);
        builder.Property(record => record.Operation).HasConversion<string>().HasMaxLength(20);
        builder.Property(record => record.Changes)
            .HasConversion(ChangesJson.Converter, ChangesJson.Comparer)
            .HasColumnType("nvarchar(max)");

        builder.HasIndex(record => record.OccurredAtUtc);
        builder.HasIndex(record => new { record.ObjectType, record.ObjectId });
        builder.HasIndex(record => new { record.ParentObjectType, record.ParentObjectId });
    }
}

/// <summary>Zakres zmiany jako JSON: <c>[{"field","old","new","personal"}]</c>.</summary>
internal static class ChangesJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static readonly ValueConverter<IReadOnlyList<AuditFieldChange>, string> Converter = new(
        changes => Serialize(changes),
        json => Deserialize(json));

    public static readonly ValueComparer<IReadOnlyList<AuditFieldChange>> Comparer = new(
        (left, right) => ReferenceEquals(left, right) || (left != null && right != null && left.SequenceEqual(right)),
        changes => changes.Aggregate(0, (hash, change) => HashCode.Combine(hash, change)),
        changes => changes.ToList());

    private static string Serialize(IReadOnlyList<AuditFieldChange> changes) =>
        JsonSerializer.Serialize(changes.Select(change => new Item(change.Field, change.OldValue, change.NewValue, change.IsPersonalData)), Options);

    private static IReadOnlyList<AuditFieldChange> Deserialize(string json) =>
        (JsonSerializer.Deserialize<Item[]>(json, Options) ?? [])
            .Select(item => new AuditFieldChange(item.Field, item.Old, item.New, item.Personal))
            .ToList();

    private sealed record Item(
        [property: JsonPropertyName("field")] string Field,
        [property: JsonPropertyName("old")] string? Old,
        [property: JsonPropertyName("new")] string? New,
        [property: JsonPropertyName("personal")] bool Personal);
}
