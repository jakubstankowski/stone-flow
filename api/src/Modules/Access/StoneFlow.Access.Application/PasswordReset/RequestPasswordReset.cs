using StoneFlow.Access.Application.Abstractions;
using StoneFlow.Access.Domain.Accounts;
using StoneFlow.Access.Domain.PasswordReset;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Access.Application.PasswordReset;

public sealed record RequestPasswordResetCommand(string? Email) : ICommand<RequestPasswordResetResult>;

public sealed record RequestPasswordResetResult(bool Accepted, FieldErrors? Errors = null);

/// <summary>
/// Prośba o link ustawienia hasła. Odpowiedź jest zawsze taka sama dla poprawnego adresu,
/// niezależnie od istnienia konta. Pierwsze konto powstaje tylko wtedy, gdy w bazie nie ma
/// żadnego konta, a adres jest adresem właściciela z konfiguracji.
/// </summary>
public sealed class RequestPasswordResetHandler(
    IAccountService accounts,
    IPasswordResetTokenStore tokens,
    IAccessUnitOfWork unitOfWork,
    IEmailSender emailSender,
    ISecurityEvents events,
    AccessSettings settings,
    TimeProvider timeProvider) : ICommandHandler<RequestPasswordResetCommand, RequestPasswordResetResult>
{
    private static readonly RequestPasswordResetResult Accepted = new(true);

    public async Task<RequestPasswordResetResult> HandleAsync(RequestPasswordResetCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Email))
        {
            return Invalid(AccessMessages.EmailRequired);
        }

        if (!EmailAddress.IsValid(command.Email))
        {
            return Invalid(AccessMessages.EmailInvalid);
        }

        var email = command.Email.Trim();
        var account = await accounts.FindByEmailAsync(email, cancellationToken)
            ?? await CreateOwnerAccountAsync(email, cancellationToken);
        if (account is null)
        {
            events.ResetRequested(null);
            return Accepted;
        }

        events.ResetRequested(account.Id);
        var now = timeProvider.GetUtcNow();
        var issuedRecently = await tokens.CountIssuedSinceAsync(account.Id, now - PasswordResetLimits.Window, cancellationToken);
        if (issuedRecently >= PasswordResetLimits.MaxRequestsPerAccount)
        {
            events.ResetSuppressedByAccountLimit(account.Id);
            return Accepted;
        }

        var token = ResetToken.Generate();
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await tokens.ReplaceActiveAsync(PasswordResetToken.Issue(account.Id, ResetToken.Hash(token), now), now, ct);
                return true;
            },
            cancellationToken);

        var message = PasswordResetEmail.Create(account.Email, PasswordResetEmail.Link(settings.AppBaseUrl, account.Id, token));
        if (await emailSender.SendAsync(message, cancellationToken) == EmailDeliveryResult.Failed)
        {
            events.ResetEmailFailed(account.Id);
        }

        return Accepted;
    }

    private async Task<AccountInfo?> CreateOwnerAccountAsync(string email, CancellationToken cancellationToken)
    {
        if (!EmailAddress.AreSame(email, settings.OwnerEmail) || await accounts.AnyAccountExistsAsync(cancellationToken))
        {
            return null;
        }

        var account = await accounts.CreateWithoutPasswordAsync(settings.OwnerEmail.Trim(), cancellationToken);
        events.AccountCreated(account.Id);
        return account;
    }

    private static RequestPasswordResetResult Invalid(string message) =>
        new(false, new FieldErrors().Add(AccessMessages.EmailField, message));
}
