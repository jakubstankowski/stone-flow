using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Infrastructure;
using StoneFlow.Shared.Infrastructure.Cqrs;

namespace StoneFlow.Shared.Tests.Cqrs;

public sealed class DispatcherTests
{
    [Fact]
    public async Task Resolves_command_handler_and_passes_cancellation()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        using var cancellation = new CancellationTokenSource();

        var result = await dispatcher.SendAsync(new EchoCommand("abc"), cancellation.Token);

        Assert.Equal("abc", result.Value);
        Assert.Equal(cancellation.Token, result.Token);
    }

    [Fact]
    public async Task Resolves_query_handler()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.QueryAsync(new DoubleQuery(21), TestContext.Current.CancellationToken);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Throws_when_handler_missing()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.SendAsync(new UnhandledCommand(), TestContext.Current.CancellationToken));

        Assert.Contains(nameof(UnhandledCommand), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Synchronous_handler_exception_is_original()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => dispatcher.SendAsync(new ThrowingCommand(Synchronously: true), TestContext.Current.CancellationToken));

        Assert.Equal("Błąd handlera", exception.Message);
        Assert.Contains(nameof(ThrowingCommandHandler), exception.StackTrace, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Faulted_task_exception_is_original()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => dispatcher.SendAsync(new ThrowingCommand(Synchronously: false), TestContext.Current.CancellationToken));

        Assert.Equal("Błąd handlera", exception.Message);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSharedInfrastructure();
        services.AddHandlersFromAssembly(typeof(DispatcherTests).Assembly);
        return services.BuildServiceProvider(validateScopes: true);
    }

    public sealed record EchoResult(string Value, CancellationToken Token);

    public sealed record EchoCommand(string Value) : ICommand<EchoResult>;

    public sealed record DoubleQuery(int Value) : IQuery<int>;

    public sealed record UnhandledCommand : ICommand<int>;

    public sealed record ThrowingCommand(bool Synchronously) : ICommand<int>;

    public sealed class ThrowingCommandHandler : ICommandHandler<ThrowingCommand, int>
    {
        public Task<int> HandleAsync(ThrowingCommand command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);
            return command.Synchronously ? throw new ArgumentException("Błąd handlera") : ThrowLaterAsync();
        }

        private static async Task<int> ThrowLaterAsync()
        {
            await Task.Yield();
            throw new ArgumentException("Błąd handlera");
        }
    }

    public sealed class EchoCommandHandler : ICommandHandler<EchoCommand, EchoResult>
    {
        public Task<EchoResult> HandleAsync(EchoCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new EchoResult(command.Value, cancellationToken));
    }

    public sealed class DoubleQueryHandler : IQueryHandler<DoubleQuery, int>
    {
        public Task<int> HandleAsync(DoubleQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(query.Value * 2);
    }
}
