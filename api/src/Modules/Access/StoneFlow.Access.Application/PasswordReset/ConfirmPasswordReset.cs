using StoneFlow.Access.Application.Abstractions;
using StoneFlow.Access.Domain.Accounts;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Access.Application.PasswordReset;

public sealed record ConfirmPasswordResetCommand(string? AccountId, string? Token, string? NewPassword)
    : ICommand<ConfirmPasswordResetResult>;

public enum ConfirmPasswordResetOutcome
{
    Succeeded,

    /// <summary>Token nieznany, wygasły, użyty, unieważniony albo niepasujący do konta.</summary>
    InvalidLink,
    ValidationFailed,
}

public sealed record ConfirmPasswordResetResult(ConfirmPasswordResetOutcome Outcome, FieldErrors? Errors = null);

/// <summary>
/// Ustawienie hasła z linku. Za słabe hasło nie zużywa tokenu; token jest zużywany atomowo
/// razem z ustawieniem hasła w jednej transakcji. Sesja nie jest tworzona.
/// </summary>
public sealed class ConfirmPasswordResetHandler(
    IAccountService accounts,
    IPasswordResetTokenStore tokens,
    IAccessUnitOfWork unitOfWork,
    ISecurityEvents events,
    TimeProvider timeProvider) : ICommandHandler<ConfirmPasswordResetCommand, ConfirmPasswordResetResult>
{
    private static readonly ConfirmPasswordResetResult InvalidLink = new(ConfirmPasswordResetOutcome.InvalidLink);

    public async Task<ConfirmPasswordResetResult> HandleAsync(ConfirmPasswordResetCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tokenHash = ResetToken.TryHash(command.Token);
        if (!Guid.TryParse(command.AccountId, out var accountId) || tokenHash is null)
        {
            events.ResetTokenRejected(null);
            return InvalidLink;
        }

        var account = await accounts.FindByIdAsync(accountId, cancellationToken);
        if (account is null)
        {
            events.ResetTokenRejected(null);
            return InvalidLink;
        }

        var violation = PasswordPolicy.Check(command.NewPassword, account.Email);
        if (violation is not null || command.NewPassword is null)
        {
            return Invalid(violation switch
            {
                PasswordViolation.TooShort => AccessMessages.NewPasswordTooShort,
                PasswordViolation.SameAsEmail => AccessMessages.NewPasswordSameAsEmail,
                _ => AccessMessages.NewPasswordRequired,
            });
        }

        var newPassword = command.NewPassword;
        ConfirmPasswordResetOutcome outcome;
        try
        {
            outcome = await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    if (!await tokens.TryConsumeAsync(accountId, tokenHash, timeProvider.GetUtcNow(), ct))
                    {
                        return ConfirmPasswordResetOutcome.InvalidLink;
                    }

                    // Odrzucenie hasła po zużyciu tokenu wycofuje całą transakcję (token zostaje ważny).
                    return await accounts.SetPasswordAsync(accountId, newPassword, ct)
                        ? ConfirmPasswordResetOutcome.Succeeded
                        : throw new PasswordRejectedException();
                },
                cancellationToken);
        }
        catch (PasswordRejectedException)
        {
            return Invalid(AccessMessages.NewPasswordRejected);
        }

        if (outcome == ConfirmPasswordResetOutcome.InvalidLink)
        {
            events.ResetTokenRejected(accountId);
            return InvalidLink;
        }

        events.PasswordSet(accountId);
        return new ConfirmPasswordResetResult(ConfirmPasswordResetOutcome.Succeeded);
    }

    private static ConfirmPasswordResetResult Invalid(string message) =>
        new(ConfirmPasswordResetOutcome.ValidationFailed, new FieldErrors().Add(AccessMessages.NewPasswordField, message));
}

/// <summary>Hasło odrzucone przez magazyn kont; wycofuje transakcję (token pozostaje nieużyty).</summary>
public sealed class PasswordRejectedException : Exception
{
    public PasswordRejectedException()
        : base("Nie udało się ustawić hasła.")
    {
    }

    public PasswordRejectedException(string message)
        : base(message)
    {
    }

    public PasswordRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
