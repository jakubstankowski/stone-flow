namespace StoneFlow.Orders.Domain.Orders;

/// <summary>
/// Klient zamówienia (value object). Wymagane są tylko rodzaj i nazwa; pozostałe dane są
/// opcjonalne. Wartości przychodzą już znormalizowane (przycięte, NIP z samych cyfr).
/// </summary>
public sealed record Customer(
    CustomerKind Kind,
    string Name,
    string? Phone,
    string? Email,
    string? Street,
    string? PostalCode,
    string? City,
    string? TaxId)
{
    public const int NameMaxLength = 200;
    public const int PhoneMaxLength = 32;
    public const int EmailMaxLength = 254;
    public const int StreetMaxLength = 200;
    public const int PostalCodeMaxLength = 16;
    public const int CityMaxLength = 100;
    public const int TaxIdMaxLength = 16;
}
