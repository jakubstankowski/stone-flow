namespace StoneFlow.Access.Domain.PasswordReset;

/// <summary>
/// Jednorazowy token resetu hasła. Przechowywany jest wyłącznie hash SHA-256 tokenu;
/// token jest ważny 30 minut i traci ważność po użyciu albo unieważnieniu.
/// </summary>
public sealed class PasswordResetToken
{
    public const int HashLength = 32;

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private PasswordResetToken(Guid id, Guid userId, byte[] tokenHash, DateTime createdAtUtc, DateTime expiresAtUtc)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public byte[] TokenHash { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? ConsumedAtUtc { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }

    public static PasswordResetToken Issue(Guid userId, byte[] tokenHash, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        if (tokenHash.Length != HashLength)
        {
            throw new ArgumentException($"Hash tokenu musi mieć {HashLength} bajty.", nameof(tokenHash));
        }

        var createdAt = now.UtcDateTime;
        return new PasswordResetToken(Guid.CreateVersion7(), userId, tokenHash, createdAt, createdAt + Lifetime);
    }
}
