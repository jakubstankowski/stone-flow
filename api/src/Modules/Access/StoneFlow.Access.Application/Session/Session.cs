using StoneFlow.Access.Application.Abstractions;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Access.Application.Session;

public sealed record LogoutCommand(Guid AccountId) : ICommand<bool>;

/// <summary>
/// Wylogowanie zmienia znacznik bezpieczeństwa, więc kończy wszystkie sesje konta (także
/// skopiowane ciasteczko i inne urządzenia), a potem usuwa ciasteczko bieżącej sesji.
/// </summary>
public sealed class LogoutHandler(IAccountService accounts, ISessionManager sessions, ISecurityEvents events)
    : ICommandHandler<LogoutCommand, bool>
{
    public async Task<bool> HandleAsync(LogoutCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await accounts.RotateSecurityStampAsync(command.AccountId, cancellationToken);
        await sessions.SignOutAsync(cancellationToken);
        events.LoggedOut(command.AccountId);
        return true;
    }
}

public sealed record SessionDto(string Email);

public sealed record GetSessionQuery(Guid AccountId) : IQuery<SessionDto?>;

public sealed class GetSessionHandler(IAccountService accounts) : IQueryHandler<GetSessionQuery, SessionDto?>
{
    public async Task<SessionDto?> HandleAsync(GetSessionQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var account = await accounts.FindByIdAsync(query.AccountId, cancellationToken);
        return account is null ? null : new SessionDto(account.Email);
    }
}
