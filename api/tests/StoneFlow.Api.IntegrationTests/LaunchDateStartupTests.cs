using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Shared.Application.LaunchDate;

namespace StoneFlow.Api.IntegrationTests;

public sealed class LaunchDateStartupTests
{
    [Fact]
    public async Task Host_fails_without_launch_date()
    {
        await using var factory = new StoneFlowApiFactory().Without("StoneFlow:LaunchDate");

        var exception = Record.Exception(() => factory.CreateClient());

        AssertValidationFailure(exception);
    }

    [Theory]
    [InlineData("03.11.2026")]
    [InlineData("2026-02-30")]
    [InlineData("2026-11-03T10:00")]
    public async Task Host_fails_with_invalid_launch_date(string value)
    {
        await using var factory = new StoneFlowApiFactory().With("StoneFlow:LaunchDate", value);

        var exception = Record.Exception(() => factory.CreateClient());

        AssertValidationFailure(exception);
    }

    [Fact]
    public async Task Launch_date_is_available_to_modules()
    {
        await using var factory = new StoneFlowApiFactory().With("StoneFlow:LaunchDate", "2026-12-01");

        var launchDate = factory.Services.GetRequiredService<ILaunchDate>();

        Assert.Equal(new DateOnly(2026, 12, 1), launchDate.Value);
    }

    [Fact]
    public void Base_appsettings_has_no_launch_date()
    {
        var path = Path.Combine(RepositoryRoot.Path, "api", "src", "StoneFlow.Api", "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var hasLaunchDate = document.RootElement.TryGetProperty("StoneFlow", out var section)
            && section.TryGetProperty("LaunchDate", out _);

        Assert.False(hasLaunchDate, "Data uruchomienia należy do konfiguracji wdrożenia, nie do appsettings.json.");
    }

    private static void AssertValidationFailure(Exception? exception)
    {
        Assert.NotNull(exception);
        var validation = Unwrap(exception).OfType<OptionsValidationException>().FirstOrDefault();
        Assert.NotNull(validation);
        Assert.Contains("StoneFlow:LaunchDate", validation.Message, StringComparison.Ordinal);
    }

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
