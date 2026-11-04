using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoneFlow.Access.Domain.Accounts;
using StoneFlow.Access.Domain.PasswordReset;

namespace StoneFlow.Access.Infrastructure.Persistence;

/// <summary>
/// Dane modułu Access w schemacie <c>access</c>: konto (jedna tabela w kształcie Identity, bez
/// ról, claimów, loginów i tokenów), tokeny resetu hasła i klucze Data Protection.
/// </summary>
public sealed class AccessDbContext(DbContextOptions<AccessDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public const string Schema = "access";

    public DbSet<AccessUser> Users => Set<AccessUser>();

    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<AccessUser>(ConfigureUser);
        modelBuilder.Entity<PasswordResetToken>(ConfigureResetToken);
        modelBuilder.Entity<DataProtectionKey>(keys => keys.ToTable("DataProtectionKeys"));
    }

    private static void ConfigureUser(EntityTypeBuilder<AccessUser> user)
    {
        user.ToTable("Users");
        user.HasKey(u => u.Id);
        user.Property(u => u.Id).ValueGeneratedNever();
        user.Property(u => u.Email).HasMaxLength(EmailAddress.MaxLength).IsRequired();
        user.Property(u => u.NormalizedEmail).HasMaxLength(EmailAddress.MaxLength).IsRequired();
        user.HasIndex(u => u.NormalizedEmail).IsUnique();
        user.Property(u => u.SecurityStamp).HasMaxLength(64).IsRequired();
        user.Property(u => u.ConcurrencyStamp).HasMaxLength(64).IsRequired().IsConcurrencyToken();
        user.Property(u => u.LockoutEndUtc).HasColumnType("datetime2");
        user.Property(u => u.CreatedAtUtc).HasColumnType("datetime2");
    }

    private static void ConfigureResetToken(EntityTypeBuilder<PasswordResetToken> token)
    {
        token.ToTable("PasswordResetTokens");
        token.HasKey(t => t.Id);
        token.Property(t => t.Id).ValueGeneratedNever();
        token.Property(t => t.TokenHash).HasColumnType($"binary({PasswordResetToken.HashLength})").IsRequired();
        token.HasIndex(t => t.TokenHash).IsUnique();
        token.HasIndex(t => new { t.UserId, t.CreatedAtUtc });
        token.HasOne<AccessUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        token.Property(t => t.CreatedAtUtc).HasColumnType("datetime2");
        token.Property(t => t.ExpiresAtUtc).HasColumnType("datetime2");
        token.Property(t => t.ConsumedAtUtc).HasColumnType("datetime2");
        token.Property(t => t.RevokedAtUtc).HasColumnType("datetime2");
    }
}

/// <summary>Konto w kształcie potrzebnym ASP.NET Core Identity (nazwa użytkownika = e-mail).</summary>
public sealed class AccessUser
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string NormalizedEmail { get; set; } = string.Empty;

    public string? PasswordHash { get; set; }

    public string SecurityStamp { get; set; } = string.Empty;

    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");

    public int AccessFailedCount { get; set; }

    public DateTime? LockoutEndUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
