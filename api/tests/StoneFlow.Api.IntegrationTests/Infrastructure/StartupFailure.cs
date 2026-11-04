using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;

namespace StoneFlow.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Deterministyczne sprawdzenie, że host nie wystartował z powodu walidacji opcji.
/// </summary>
/// <remarks>
/// Przy minimal hostingu <c>WebApplicationFactory</c> startuje host przez punkt wejścia
/// programu. Gdy <c>ValidateOnStart</c> przerwie start, punkt wejścia zwalnia host, a
/// fabryka może zgłosić wyjątek wtórny (np. o zwolnionym hoście) bez
/// <see cref="OptionsValidationException"/> w łańcuchu. Host zawsze loguje jednak
/// pierwotny błąd („Hosting failed to start”) przed zamknięciem, więc helper szuka go
/// w łańcuchu wyjątku albo w logach fabryki.
/// </remarks>
internal static class StartupFailure
{
    public static void AssertOptionsValidation(StoneFlowApiFactory factory, string configurationKey, string? forbiddenValue = null)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        var validation = Unwrap(exception).OfType<OptionsValidationException>().FirstOrDefault()
            ?? FromLogs(factory.Logs);
        Assert.True(validation is not null, $"Brak OptionsValidationException w wyjątku ani w logach startu. Wyjątek: {exception}");
        Assert.Contains(configurationKey, validation.Message, StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(forbiddenValue))
        {
            Assert.DoesNotContain(forbiddenValue, validation.Message, StringComparison.Ordinal);
        }
    }

    private static OptionsValidationException? FromLogs(FakeLogCollector logs) =>
        logs.GetSnapshot()
            .Select(record => record.Exception)
            .Where(exception => exception is not null)
            .SelectMany(exception => Unwrap(exception!))
            .OfType<OptionsValidationException>()
            .FirstOrDefault();

    private static IEnumerable<Exception> Unwrap(Exception exception)
    {
        yield return exception;

        var inner = exception is AggregateException aggregate
            ? aggregate.InnerExceptions.ToList()
            : exception.InnerException is { } single ? [single] : [];
        foreach (var child in inner.SelectMany(Unwrap))
        {
            yield return child;
        }
    }
}
