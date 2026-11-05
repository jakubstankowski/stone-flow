namespace StoneFlow.Orders.Domain.Orders;

/// <summary>
/// Dane operacyjne zamówienia (value object): dostawcy, referencje ZO, faktura, flagi i uwagi.
/// Listy są porównywane z kolejnością; zmiana kolejności jest zmianą.
/// </summary>
public sealed record OperationalData(
    IReadOnlyList<string> Suppliers,
    IReadOnlyList<string> ZoReferences,
    string? InvoiceNumber,
    bool HasPhotos,
    bool HasReview,
    string? Notes)
{
    public const int MaxSuppliers = 10;
    public const int SupplierMaxLength = 100;
    public const int MaxZoReferences = 10;
    public const int ZoReferenceMaxLength = 64;
    public const int InvoiceNumberMaxLength = 64;
    public const int NotesMaxLength = 2000;

    public static OperationalData Empty { get; } = new([], [], null, false, false, null);

    public bool Equals(OperationalData? other) =>
        other is not null
        && Suppliers.SequenceEqual(other.Suppliers, StringComparer.Ordinal)
        && ZoReferences.SequenceEqual(other.ZoReferences, StringComparer.Ordinal)
        && string.Equals(InvoiceNumber, other.InvoiceNumber, StringComparison.Ordinal)
        && HasPhotos == other.HasPhotos
        && HasReview == other.HasReview
        && string.Equals(Notes, other.Notes, StringComparison.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var supplier in Suppliers)
        {
            hash.Add(supplier, StringComparer.Ordinal);
        }

        foreach (var reference in ZoReferences)
        {
            hash.Add(reference, StringComparer.Ordinal);
        }

        hash.Add(InvoiceNumber, StringComparer.Ordinal);
        hash.Add(HasPhotos);
        hash.Add(HasReview);
        hash.Add(Notes, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
