using Microsoft.Extensions.Logging;
using StoneFlow.Access.Application.Abstractions;

namespace StoneFlow.Access.Infrastructure.Logging;

/// <summary>
/// Zdarzenia bezpieczeństwa w logach: wyłącznie identyfikator konta (bez e-maila, hasła i tokenu);
/// korelację dodaje zakres logów hosta.
/// </summary>
internal sealed partial class SecurityEventLog(ILogger<SecurityEventLog> logger) : ISecurityEvents
{
    public void LoginSucceeded(Guid accountId) => LogLoginSucceeded(accountId);

    public void LoginFailed(Guid? accountId) => LogLoginFailed(accountId);

    public void LockedOut(Guid accountId) => LogLockedOut(accountId);

    public void LoggedOut(Guid accountId) => LogLoggedOut(accountId);

    public void AccountCreated(Guid accountId) => LogAccountCreated(accountId);

    public void ResetRequested(Guid? accountId) => LogResetRequested(accountId);

    public void ResetSuppressedByAccountLimit(Guid accountId) => LogResetSuppressed(accountId);

    public void ResetEmailFailed(Guid accountId) => LogResetEmailFailed(accountId);

    public void ResetTokenRejected(Guid? accountId) => LogResetTokenRejected(accountId);

    public void PasswordSet(Guid accountId) => LogPasswordSet(accountId);

    [LoggerMessage(EventId = 6000, Level = LogLevel.Information, Message = "Logowanie udane, konto {AccountId}.")]
    private partial void LogLoginSucceeded(Guid accountId);

    [LoggerMessage(EventId = 6001, Level = LogLevel.Warning, Message = "Logowanie nieudane, konto {AccountId}.")]
    private partial void LogLoginFailed(Guid? accountId);

    [LoggerMessage(EventId = 6002, Level = LogLevel.Warning, Message = "Konto {AccountId} zablokowane po nieudanych próbach logowania.")]
    private partial void LogLockedOut(Guid accountId);

    [LoggerMessage(EventId = 6003, Level = LogLevel.Information, Message = "Wylogowanie, konto {AccountId}; wszystkie sesje unieważnione.")]
    private partial void LogLoggedOut(Guid accountId);

    [LoggerMessage(EventId = 6004, Level = LogLevel.Information, Message = "Utworzono konto właściciela {AccountId}.")]
    private partial void LogAccountCreated(Guid accountId);

    [LoggerMessage(EventId = 6010, Level = LogLevel.Information, Message = "Prośba o reset hasła, konto {AccountId}.")]
    private partial void LogResetRequested(Guid? accountId);

    [LoggerMessage(EventId = 6011, Level = LogLevel.Warning, Message = "Prośba o reset hasła pominięta (limit konta), konto {AccountId}.")]
    private partial void LogResetSuppressed(Guid accountId);

    [LoggerMessage(EventId = 6012, Level = LogLevel.Error, Message = "Nie wysłano linku resetu hasła, konto {AccountId}.")]
    private partial void LogResetEmailFailed(Guid accountId);

    [LoggerMessage(EventId = 6013, Level = LogLevel.Warning, Message = "Odrzucony link resetu hasła, konto {AccountId}.")]
    private partial void LogResetTokenRejected(Guid? accountId);

    [LoggerMessage(EventId = 6014, Level = LogLevel.Information, Message = "Ustawiono hasło, konto {AccountId}; sesje unieważnione.")]
    private partial void LogPasswordSet(Guid accountId);
}
