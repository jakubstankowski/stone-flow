using StoneFlow.Access.Application.Abstractions;
using StoneFlow.Access.Domain.Accounts;
using StoneFlow.Access.Domain.PasswordReset;

namespace StoneFlow.Access.Tests.Application;

internal sealed class FakeAccount(Guid id, string email)
{
    public Guid Id { get; } = id;

    public string Email { get; } = email;

    public string? Password { get; set; }
}

internal sealed class FakeAccounts : IAccountService
{
    private readonly List<FakeAccount> _accounts = [];

    public IReadOnlyList<FakeAccount> All => _accounts;

    public FakeAccount Add(string email)
    {
        var account = new FakeAccount(Guid.NewGuid(), email);
        _accounts.Add(account);
        return account;
    }

    public Task<AccountInfo?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.Where(a => EmailAddress.AreSame(a.Email, email)).Select(Info).FirstOrDefault());

    public Task<AccountInfo?> FindByIdAsync(Guid accountId, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.Where(a => a.Id == accountId).Select(Info).FirstOrDefault());

    public Task<bool> AnyAccountExistsAsync(CancellationToken cancellationToken) => Task.FromResult(_accounts.Count > 0);

    public Task<AccountInfo> CreateWithoutPasswordAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(Info(Add(email)));

    public Task<CredentialCheck> VerifyCredentialsAsync(string email, string password, CancellationToken cancellationToken)
    {
        var account = _accounts.FirstOrDefault(a => EmailAddress.AreSame(a.Email, email));
        return Task.FromResult(account is not null && account.Password == password
            ? new CredentialCheck(CredentialStatus.Succeeded, account.Id, false)
            : new CredentialCheck(CredentialStatus.InvalidCredentials, account?.Id, false));
    }

    public Task<bool> SetPasswordAsync(Guid accountId, string newPassword, CancellationToken cancellationToken)
    {
        var account = _accounts.Single(a => a.Id == accountId);
        account.Password = newPassword;
        return Task.FromResult(true);
    }

    public Task RotateSecurityStampAsync(Guid accountId, CancellationToken cancellationToken) => Task.CompletedTask;

    private static AccountInfo Info(FakeAccount account) => new(account.Id, account.Email);
}

internal sealed class FakeTokens : IPasswordResetTokenStore
{
    private readonly HashSet<Guid> _consumed = [];

    public List<PasswordResetToken> Issued { get; } = [];

    public int ConsumeCalls { get; private set; }

    public Task<int> CountIssuedSinceAsync(Guid accountId, DateTimeOffset since, CancellationToken cancellationToken) =>
        Task.FromResult(Issued.Count(t => t.UserId == accountId && t.CreatedAtUtc >= since.UtcDateTime));

    public Task ReplaceActiveAsync(PasswordResetToken token, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Issued.Add(token);
        return Task.CompletedTask;
    }

    public Task<bool> TryConsumeAsync(Guid accountId, byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ConsumeCalls++;
        var token = Issued.LastOrDefault(t => t.UserId == accountId && t.TokenHash.SequenceEqual(tokenHash));
        if (token is null || token.ExpiresAtUtc <= now.UtcDateTime || !_consumed.Add(token.Id))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }
}

internal sealed class ImmediateUnitOfWork : IAccessUnitOfWork
{
    public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken) =>
        work(cancellationToken);
}

internal sealed class FakeEmail : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];

    public EmailDeliveryResult Result { get; set; } = EmailDeliveryResult.Sent;

    public Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Sent.Add(message);
        return Task.FromResult(Result);
    }
}

internal sealed class FakeEvents : ISecurityEvents
{
    public List<string> Names { get; } = [];

    public void LoginSucceeded(Guid accountId) => Names.Add(nameof(LoginSucceeded));

    public void LoginFailed(Guid? accountId) => Names.Add(nameof(LoginFailed));

    public void LockedOut(Guid accountId) => Names.Add(nameof(LockedOut));

    public void LoggedOut(Guid accountId) => Names.Add(nameof(LoggedOut));

    public void AccountCreated(Guid accountId) => Names.Add(nameof(AccountCreated));

    public void ResetRequested(Guid? accountId) => Names.Add(nameof(ResetRequested));

    public void ResetSuppressedByAccountLimit(Guid accountId) => Names.Add(nameof(ResetSuppressedByAccountLimit));

    public void ResetEmailFailed(Guid accountId) => Names.Add(nameof(ResetEmailFailed));

    public void ResetTokenRejected(Guid? accountId) => Names.Add(nameof(ResetTokenRejected));

    public void PasswordSet(Guid accountId) => Names.Add(nameof(PasswordSet));
}
