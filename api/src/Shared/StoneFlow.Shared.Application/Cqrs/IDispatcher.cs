namespace StoneFlow.Shared.Application.Cqrs;

/// <summary>Przekazuje komendy i zapytania do zarejestrowanych handlerów.</summary>
public interface IDispatcher
{
    Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken);

    Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken);
}
