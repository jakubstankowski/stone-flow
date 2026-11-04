using Microsoft.EntityFrameworkCore;
using StoneFlow.Access.Application.Abstractions;
using StoneFlow.Access.Domain.PasswordReset;

namespace StoneFlow.Access.Infrastructure.Persistence;

internal sealed class PasswordResetTokenStore(AccessDbContext context) : IPasswordResetTokenStore
{
    public Task<int> CountIssuedSinceAsync(Guid accountId, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var sinceUtc = since.UtcDateTime;
        return context.PasswordResetTokens.CountAsync(
            token => token.UserId == accountId && token.CreatedAtUtc >= sinceUtc,
            cancellationToken);
    }

    public async Task ReplaceActiveAsync(PasswordResetToken token, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        var nowUtc = now.UtcDateTime;
        await context.PasswordResetTokens
            .Where(existing => existing.UserId == token.UserId
                && existing.ConsumedAtUtc == null
                && existing.RevokedAtUtc == null
                && existing.ExpiresAtUtc > nowUtc)
            .ExecuteUpdateAsync(set => set.SetProperty(existing => existing.RevokedAtUtc, nowUtc), cancellationToken);

        context.PasswordResetTokens.Add(token);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryConsumeAsync(Guid accountId, byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);

        var nowUtc = now.UtcDateTime;
        var consumed = await context.PasswordResetTokens
            .Where(token => token.UserId == accountId
                && token.TokenHash == tokenHash
                && token.ConsumedAtUtc == null
                && token.RevokedAtUtc == null
                && token.ExpiresAtUtc > nowUtc)
            .ExecuteUpdateAsync(set => set.SetProperty(token => token.ConsumedAtUtc, nowUtc), cancellationToken);

        return consumed == 1;
    }
}

internal sealed class AccessUnitOfWork(AccessDbContext context) : IAccessUnitOfWork
{
    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var result = await work(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
