using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StoneFlow.Access.Application.Abstractions;
using StoneFlow.Access.Infrastructure.Persistence;

namespace StoneFlow.Access.Infrastructure.Identity;

internal sealed class IdentityAccountService(
    UserManager<AccessUser> userManager,
    SignInManager<AccessUser> signInManager,
    AccessDbContext context) : IAccountService
{
    /// <summary>Hash porównywany, gdy konto nie istnieje lub nie ma hasła — wyrównuje czas odpowiedzi.</summary>
    private static readonly Lazy<string> TimingHash = new(() =>
        new PasswordHasher<AccessUser>().HashPassword(new AccessUser(), Guid.NewGuid().ToString("N")));

    public async Task<AccountInfo?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        return user is null ? null : ToInfo(user);
    }

    public async Task<AccountInfo?> FindByIdAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var user = await context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == accountId, cancellationToken);
        return user is null ? null : ToInfo(user);
    }

    public Task<bool> AnyAccountExistsAsync(CancellationToken cancellationToken) =>
        context.Users.AnyAsync(cancellationToken);

    public async Task<AccountInfo> CreateWithoutPasswordAsync(string email, CancellationToken cancellationToken)
    {
        var user = new AccessUser { Id = Guid.CreateVersion7(), Email = email };
        var result = await userManager.CreateAsync(user);
        if (result.Succeeded)
        {
            return ToInfo(user);
        }

        // Równoczesna pierwsza prośba utworzyła już konto — zwracamy istniejące.
        var existing = await userManager.FindByEmailAsync(email);
        return existing is not null
            ? ToInfo(existing)
            : throw new InvalidOperationException("Nie udało się utworzyć konta właściciela.");
    }

    public async Task<CredentialCheck> VerifyCredentialsAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            SpendHashingTime(password);
            return new CredentialCheck(CredentialStatus.InvalidCredentials, null, BecameLockedOut: false);
        }

        if (user.PasswordHash is null)
        {
            SpendHashingTime(password);
        }

        var wasLockedOut = await userManager.IsLockedOutAsync(user);
        if (wasLockedOut && user.PasswordHash is not null)
        {
            // Przy blokadzie Identity nie sprawdza hasła — wyrównujemy czas, aby nie zdradzić istnienia konta.
            SpendHashingTime(password);
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            return new CredentialCheck(CredentialStatus.Succeeded, user.Id, BecameLockedOut: false);
        }

        return result.IsLockedOut
            ? new CredentialCheck(CredentialStatus.LockedOut, user.Id, BecameLockedOut: !wasLockedOut)
            : new CredentialCheck(CredentialStatus.InvalidCredentials, user.Id, BecameLockedOut: false);
    }

    public async Task<bool> SetPasswordAsync(Guid accountId, string newPassword, CancellationToken cancellationToken)
    {
        var user = await context.Users.SingleOrDefaultAsync(u => u.Id == accountId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        foreach (var validator in userManager.PasswordValidators)
        {
            if (!(await validator.ValidateAsync(userManager, user, newPassword)).Succeeded)
            {
                return false;
            }
        }

        user.PasswordHash = userManager.PasswordHasher.HashPassword(user, newPassword);
        user.AccessFailedCount = 0;
        user.LockoutEndUtc = null;

        // Nowy znacznik bezpieczeństwa unieważnia wszystkie istniejące sesje.
        return (await userManager.UpdateSecurityStampAsync(user)).Succeeded;
    }

    public async Task RotateSecurityStampAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var user = await context.Users.SingleOrDefaultAsync(u => u.Id == accountId, cancellationToken);
        if (user is null)
        {
            return;
        }

        // Bez nowego znacznika skopiowane ciasteczko pozostałoby ważne — błąd przerywa wylogowanie (fail closed).
        var result = await userManager.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Nie udało się unieważnić sesji konta.");
        }
    }

    private void SpendHashingTime(string password) =>
        userManager.PasswordHasher.VerifyHashedPassword(new AccessUser(), TimingHash.Value, password);

    private static AccountInfo ToInfo(AccessUser user) => new(user.Id, user.Email);
}
