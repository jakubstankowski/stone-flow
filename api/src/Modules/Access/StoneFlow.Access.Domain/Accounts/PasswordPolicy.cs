namespace StoneFlow.Access.Domain.Accounts;

public enum PasswordViolation
{
    Missing,
    TooShort,
    SameAsEmail,
}

/// <summary>Zasady hasła: co najmniej 12 znaków, bez reguł składu, różne od adresu e-mail.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;

    public static PasswordViolation? Check(string? password, string email)
    {
        ArgumentNullException.ThrowIfNull(email);

        if (string.IsNullOrEmpty(password))
        {
            return PasswordViolation.Missing;
        }

        if (password.Length < MinLength)
        {
            return PasswordViolation.TooShort;
        }

        return string.Equals(password.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase)
            ? PasswordViolation.SameAsEmail
            : null;
    }
}
