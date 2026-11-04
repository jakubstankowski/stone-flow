using System.Net.Mail;

namespace StoneFlow.Access.Domain.Accounts;

/// <summary>Reguły adresu e-mail konta: normalizacja do porównań i walidacja formatu.</summary>
public static class EmailAddress
{
    public const int MaxLength = 256;

    /// <summary>Postać do porównań i unikalności: bez spacji na brzegach, wielkie litery (niezmienne).</summary>
    public static string Normalize(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Poprawny adres bez nazwy wyświetlanej i dodatków: <c>MailAddress</c> musi odczytać
    /// dokładnie podany (przycięty) tekst.
    /// </summary>
    public static bool IsValid(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var trimmed = email.Trim();
        return trimmed.Length <= MaxLength
            && MailAddress.TryCreate(trimmed, out var address)
            && string.Equals(address.Address, trimmed, StringComparison.Ordinal);
    }

    public static bool AreSame(string first, string second) =>
        string.Equals(Normalize(first), Normalize(second), StringComparison.Ordinal);
}
