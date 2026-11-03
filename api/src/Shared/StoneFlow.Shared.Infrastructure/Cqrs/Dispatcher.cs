using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Shared.Infrastructure.Cqrs;

internal sealed class Dispatcher(IServiceProvider serviceProvider) : IDispatcher
{
    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResult));
        var handler = Resolve(handlerType, command.GetType());

        return Invoke<TResult>(handlerType, handler, command, cancellationToken);
    }

    public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var handlerType = typeof(IQueryHandler<,>).MakeGenericType(query.GetType(), typeof(TResult));
        var handler = Resolve(handlerType, query.GetType());

        return Invoke<TResult>(handlerType, handler, query, cancellationToken);
    }

    private object Resolve(Type handlerType, Type requestType) =>
        serviceProvider.GetService(handlerType)
        ?? throw new InvalidOperationException(
            $"Brak zarejestrowanego handlera dla '{requestType.FullName}'.");

    private static Task<TResult> Invoke<TResult>(
        Type handlerType,
        object handler,
        object request,
        CancellationToken cancellationToken)
    {
        var method = handlerType.GetMethod("HandleAsync")
            ?? throw new InvalidOperationException($"Typ '{handlerType.FullName}' nie ma metody HandleAsync.");

        return (Task<TResult>)method.Invoke(handler, [request, cancellationToken])!;
    }
}
