namespace StoneFlow.Access.Domain.Sessions;

/// <summary>Czas życia sesji: 60 minut bezczynności i najwyżej 12 godzin od zalogowania.</summary>
public static class SessionPolicy
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(60);

    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(12);

    public static bool IsWithinAbsoluteLifetime(DateTimeOffset authenticatedAt, DateTimeOffset now) =>
        authenticatedAt <= now && now - authenticatedAt < AbsoluteLifetime;
}
