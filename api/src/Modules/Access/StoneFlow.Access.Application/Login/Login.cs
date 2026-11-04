using StoneFlow.Access.Application.Abstractions;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Access.Application.Login;

public sealed record LoginCommand(string? Email, string? Password) : ICommand<LoginResult>;

public enum LoginOutcome
{
    Succeeded,
    ValidationFailed,

    /// <summary>Błędne hasło, nieistniejące konto, konto bez hasła albo blokada — bez rozróżnienia.</summary>
    InvalidCredentials,
}

public sealed record LoginResult(LoginOutcome Outcome, FieldErrors? Errors = null);

public sealed class LoginHandler(
    IAccountService accounts,
    ISessionManager sessions,
    ISecurityEvents events,
    TimeProvider timeProvider) : ICommandHandler<LoginCommand, LoginResult>
{
    public async Task<LoginResult> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new FieldErrors();
        if (string.IsNullOrWhiteSpace(command.Email))
        {
            errors.Add(AccessMessages.EmailField, AccessMessages.EmailRequired);
        }

        if (string.IsNullOrEmpty(command.Password))
        {
            errors.Add(AccessMessages.PasswordField, AccessMessages.PasswordRequired);
        }

        if (!errors.IsEmpty || command.Email is null || command.Password is null)
        {
            return new LoginResult(LoginOutcome.ValidationFailed, errors);
        }

        var check = await accounts.VerifyCredentialsAsync(command.Email.Trim(), command.Password, cancellationToken);
        switch (check.Status)
        {
            case CredentialStatus.Succeeded when check.AccountId is { } accountId:
                await sessions.SignInAsync(accountId, timeProvider.GetUtcNow(), cancellationToken);
                events.LoginSucceeded(accountId);
                return new LoginResult(LoginOutcome.Succeeded);

            case CredentialStatus.LockedOut when check.AccountId is { } lockedId:
                if (check.BecameLockedOut)
                {
                    events.LockedOut(lockedId);
                }
                else
                {
                    events.LoginFailed(lockedId);
                }

                return new LoginResult(LoginOutcome.InvalidCredentials);

            default:
                events.LoginFailed(check.AccountId);
                return new LoginResult(LoginOutcome.InvalidCredentials);
        }
    }
}
