namespace StoneFlow.Access.Domain.Accounts;

/// <summary>Blokada konta po kolejnych nieudanych logowaniach.</summary>
public static class LockoutPolicy
{
    public const int MaxFailedAttempts = 5;

    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(15);
}
