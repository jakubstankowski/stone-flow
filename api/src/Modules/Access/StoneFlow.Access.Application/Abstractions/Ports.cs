using StoneFlow.Access.Domain.PasswordReset;

namespace StoneFlow.Access.Application.Abstractions;

/// <summary>Konto właściciela widziane przez przypadki użycia (bez hasła i znaczników).</summary>
public sealed record AccountInfo(Guid Id, string Email);

public enum CredentialStatus
{
    Succeeded,
    InvalidCredentials,
    LockedOut,
}

public sealed record CredentialCheck(CredentialStatus Status, Guid? AccountId, bool BecameLockedOut);

/// <summary>Konto: odczyt, utworzenie, sprawdzenie hasła z blokadą, ustawienie hasła.</summary>
public interface IAccountService
{
    Task<AccountInfo?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    Task<AccountInfo?> FindByIdAsync(Guid accountId, CancellationToken cancellationToken);

    Task<bool> AnyAccountExistsAsync(CancellationToken cancellationToken);

    /// <summary>Tworzy konto bez hasła; przy równoczesnym utworzeniu zwraca istniejące konto.</summary>
    Task<AccountInfo> CreateWithoutPasswordAsync(string email, CancellationToken cancellationToken);

    /// <summary>Sprawdza hasło, licząc nieudane próby i blokadę; czas odpowiedzi nie zdradza istnienia konta.</summary>
    Task<CredentialCheck> VerifyCredentialsAsync(string email, string password, CancellationToken cancellationToken);

    /// <summary>Ustawia hasło, zdejmuje blokadę i zmienia znacznik bezpieczeństwa (unieważnia sesje).</summary>
    Task<bool> SetPasswordAsync(Guid accountId, string newPassword, CancellationToken cancellationToken);

    /// <summary>Zmienia znacznik bezpieczeństwa: wszystkie istniejące sesje tracą ważność.</summary>
    Task RotateSecurityStampAsync(Guid accountId, CancellationToken cancellationToken);
}

/// <summary>Sesja bieżącego żądania (ciasteczko).</summary>
public interface ISessionManager
{
    Task SignInAsync(Guid accountId, DateTimeOffset authenticatedAt, CancellationToken cancellationToken);

    Task SignOutAsync(CancellationToken cancellationToken);
}

public interface IPasswordResetTokenStore
{
    Task<int> CountIssuedSinceAsync(Guid accountId, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Unieważnia aktywne tokeny konta i zapisuje nowy.</summary>
    Task ReplaceActiveAsync(PasswordResetToken token, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Atomowo oznacza token jako użyty; <c>false</c>, gdy nieznany, wygasły, użyty lub unieważniony.</summary>
    Task<bool> TryConsumeAsync(Guid accountId, byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IAccessUnitOfWork
{
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);
}

public sealed record EmailMessage(string To, string Subject, string TextBody);

public enum EmailDeliveryResult
{
    Sent,
    Failed,
}

public interface IEmailSender
{
    Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Zdarzenia bezpieczeństwa. Przyjmują tylko identyfikator konta — nigdy e-mail, hasło ani token.</summary>
public interface ISecurityEvents
{
    void LoginSucceeded(Guid accountId);

    void LoginFailed(Guid? accountId);

    void LockedOut(Guid accountId);

    void LoggedOut(Guid accountId);

    void AccountCreated(Guid accountId);

    void ResetRequested(Guid? accountId);

    void ResetSuppressedByAccountLimit(Guid accountId);

    void ResetEmailFailed(Guid accountId);

    void ResetTokenRejected(Guid? accountId);

    void PasswordSet(Guid accountId);
}

/// <summary>Ustawienia modułu z walidowanej konfiguracji wdrożenia.</summary>
public sealed record AccessSettings(string OwnerEmail, Uri AppBaseUrl);
