using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StoneFlow.Access.Infrastructure.Persistence;

namespace StoneFlow.Access.Infrastructure.Identity;

/// <summary>
/// Magazyn Identity ograniczony do jednej tabeli: hasło, znacznik bezpieczeństwa, e-mail
/// (jednocześnie nazwa użytkownika) i blokada. Bez ról, claimów, loginów zewnętrznych i tokenów.
/// </summary>
internal sealed class AccessUserStore(AccessDbContext context, TimeProvider timeProvider) :
    IUserPasswordStore<AccessUser>,
    IUserSecurityStampStore<AccessUser>,
    IUserEmailStore<AccessUser>,
    IUserLockoutStore<AccessUser>
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public async Task<IdentityResult> CreateAsync(AccessUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        user.CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        context.Users.Add(user);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return IdentityResult.Success;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation })
        {
            context.ChangeTracker.Clear();
            return IdentityResult.Failed(new IdentityErrorDescriber().DuplicateEmail(user.Email));
        }
    }

    public async Task<IdentityResult> UpdateAsync(AccessUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return IdentityResult.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());
        }
    }

    public async Task<IdentityResult> DeleteAsync(AccessUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        context.Users.Remove(user);
        await context.SaveChangesAsync(cancellationToken);
        return IdentityResult.Success;
    }

    public Task<AccessUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
        Guid.TryParse(userId, out var id)
            ? context.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken)
            : Task.FromResult<AccessUser?>(null);

    public Task<AccessUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        FindByEmailAsync(normalizedUserName, cancellationToken);

    public Task<AccessUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        context.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

    public Task<string> GetUserIdAsync(AccessUser user, CancellationToken cancellationToken) =>
        Task.FromResult(Required(user).Id.ToString("D"));

    public Task<string?> GetUserNameAsync(AccessUser user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(Required(user).Email);

    public Task SetUserNameAsync(AccessUser user, string? userName, CancellationToken cancellationToken)
    {
        Required(user).Email = userName ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(AccessUser user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(Required(user).NormalizedEmail);

    public Task SetNormalizedUserNameAsync(AccessUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        Required(user).NormalizedEmail = normalizedName ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task SetPasswordHashAsync(AccessUser user, string? passwordHash, CancellationToken cancellationToken)
    {
        Required(user).PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(AccessUser user, CancellationToken cancellationToken) =>
        Task.FromResult(Required(user).PasswordHash);

    public Task<bool> HasPasswordAsync(AccessUser user, CancellationToken cancellationToken) =>
        Task.FromResult(Required(user).PasswordHash is not null);

    public Task SetSecurityStampAsync(AccessUser user, string stamp, CancellationToken cancellationToken)
    {
        Required(user).SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(AccessUser user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(Required(user).SecurityStamp);

    public Task SetEmailAsync(AccessUser user, string? email, CancellationToken cancellationToken)
    {
        Required(user).Email = email ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(AccessUser user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(Required(user).Email);

    /// <summary>Potwierdzanie e-maila nie jest używane — adres pochodzi z konfiguracji wdrożenia.</summary>
    public Task<bool> GetEmailConfirmedAsync(AccessUser user, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task SetEmailConfirmedAsync(AccessUser user, bool confirmed, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<string?> GetNormalizedEmailAsync(AccessUser user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(Required(user).NormalizedEmail);

    public Task SetNormalizedEmailAsync(AccessUser user, string? normalizedEmail, CancellationToken cancellationToken)
    {
        Required(user).NormalizedEmail = normalizedEmail ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(AccessUser user, CancellationToken cancellationToken) =>
        Task.FromResult(Required(user).LockoutEndUtc is { } end
            ? new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Utc))
            : (DateTimeOffset?)null);

    public Task SetLockoutEndDateAsync(AccessUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        Required(user).LockoutEndUtc = lockoutEnd?.UtcDateTime;
        return Task.CompletedTask;
    }

    public Task<int> IncrementAccessFailedCountAsync(AccessUser user, CancellationToken cancellationToken) =>
        Task.FromResult(++Required(user).AccessFailedCount);

    public Task ResetAccessFailedCountAsync(AccessUser user, CancellationToken cancellationToken)
    {
        Required(user).AccessFailedCount = 0;
        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(AccessUser user, CancellationToken cancellationToken) =>
        Task.FromResult(Required(user).AccessFailedCount);

    /// <summary>Blokada jest zawsze włączona dla jedynego konta.</summary>
    public Task<bool> GetLockoutEnabledAsync(AccessUser user, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task SetLockoutEnabledAsync(AccessUser user, bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose()
    {
        // Kontekst jest zarządzany przez kontener DI.
    }

    private static AccessUser Required(AccessUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user;
    }
}
