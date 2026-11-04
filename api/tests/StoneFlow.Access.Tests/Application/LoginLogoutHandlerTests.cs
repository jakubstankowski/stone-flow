using Microsoft.Extensions.Time.Testing;
using StoneFlow.Access.Application;
using StoneFlow.Access.Application.Abstractions;
using StoneFlow.Access.Application.Login;
using StoneFlow.Access.Application.Session;

namespace StoneFlow.Access.Tests.Application;

public sealed class LoginHandlerTests
{
    private readonly FakeAccounts _accounts = new();
    private readonly FakeSessions _sessions = new();
    private readonly FakeEvents _events = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 4, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Success_signs_in_with_the_login_time()
    {
        var account = _accounts.Add("owner@example.com");
        account.Password = "dobre-haslo-12";

        var result = await Handle("owner@example.com", "dobre-haslo-12");

        Assert.Equal(LoginOutcome.Succeeded, result.Outcome);
        Assert.Equal((account.Id, _time.GetUtcNow()), _sessions.SignedIn);
        Assert.Contains(nameof(ISecurityEvents.LoginSucceeded), _events.Names);
    }

    [Fact]
    public async Task Unknown_account_and_wrong_password_return_the_same_generic_failure()
    {
        _accounts.Add("owner@example.com").Password = "dobre-haslo-12";

        var unknown = await Handle("nikt@example.com", "dobre-haslo-12");
        var wrong = await Handle("owner@example.com", "zle-haslo-123");

        Assert.Equal(LoginOutcome.InvalidCredentials, unknown.Outcome);
        Assert.Equal(LoginOutcome.InvalidCredentials, wrong.Outcome);
        Assert.Null(_sessions.SignedIn);
    }

    [Fact]
    public async Task Missing_fields_return_validation_errors()
    {
        var result = await Handle(" ", "");

        Assert.Equal(LoginOutcome.ValidationFailed, result.Outcome);
        var errors = result.Errors!.ToDictionary();
        Assert.Equal([AccessMessages.EmailRequired], errors[AccessMessages.EmailField]);
        Assert.Equal([AccessMessages.PasswordRequired], errors[AccessMessages.PasswordField]);
    }

    private Task<LoginResult> Handle(string email, string password) =>
        new LoginHandler(_accounts, _sessions, _events, _time).HandleAsync(new LoginCommand(email, password), CancellationToken.None);
}

public sealed class LogoutHandlerTests
{
    [Fact]
    public async Task Rotates_the_security_stamp_before_signing_out()
    {
        var calls = new List<string>();
        var accounts = new RecordingAccounts(calls);
        var sessions = new FakeSessions(calls);
        var accountId = Guid.NewGuid();

        await new LogoutHandler(accounts, sessions, new FakeEvents()).HandleAsync(new LogoutCommand(accountId), CancellationToken.None);

        Assert.Equal([$"rotate:{accountId}", "signout"], calls);
    }

    private sealed class RecordingAccounts(List<string> calls) : IAccountService
    {
        public Task<AccountInfo?> FindByEmailAsync(string email, CancellationToken cancellationToken) => Task.FromResult<AccountInfo?>(null);

        public Task<AccountInfo?> FindByIdAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult<AccountInfo?>(null);

        public Task<bool> AnyAccountExistsAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<AccountInfo> CreateWithoutPasswordAsync(string email, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CredentialCheck> VerifyCredentialsAsync(string email, string password, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> SetPasswordAsync(Guid accountId, string newPassword, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RotateSecurityStampAsync(Guid accountId, CancellationToken cancellationToken)
        {
            calls.Add($"rotate:{accountId}");
            return Task.CompletedTask;
        }
    }
}

internal sealed class FakeSessions(List<string>? calls = null) : ISessionManager
{
    public (Guid AccountId, DateTimeOffset At)? SignedIn { get; private set; }

    public Task SignInAsync(Guid accountId, DateTimeOffset authenticatedAt, CancellationToken cancellationToken)
    {
        SignedIn = (accountId, authenticatedAt);
        return Task.CompletedTask;
    }

    public Task SignOutAsync(CancellationToken cancellationToken)
    {
        calls?.Add("signout");
        return Task.CompletedTask;
    }
}
