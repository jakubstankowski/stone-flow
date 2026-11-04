namespace StoneFlow.Access.Domain.PasswordReset;

/// <summary>Limit wysyłki linków dla jednego konta, niezależny od limitu żądań na adres IP.</summary>
public static class PasswordResetLimits
{
    public const int MaxRequestsPerAccount = 3;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
}
