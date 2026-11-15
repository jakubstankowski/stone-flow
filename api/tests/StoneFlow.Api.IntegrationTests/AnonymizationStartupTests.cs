using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Orders.Application.Abstractions;

namespace StoneFlow.Api.IntegrationTests;

/// <summary>
/// SF-22: okres retencji z konfiguracji wdrożenia (D-09) — brak, pusta albo biała wartość wyłącza
/// funkcję, wartość spoza 1–1200 zatrzymuje start z komunikatem bez wartości; żaden plik repozytorium
/// nie ustawia domyślnej długości okresu.
/// </summary>
public sealed partial class AnonymizationStartupTests
{
    private const string Key = "StoneFlow:Anonymization:RetentionMonths";

    [Fact]
    public async Task Missing_key_starts_with_feature_disabled()
    {
        await using var factory = new StoneFlowApiFactory().Without(Key);

        Assert.Null(factory.Services.GetRequiredService<IAnonymizationSettings>().RetentionMonths);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_or_blank_value_starts_with_feature_disabled(string value)
    {
        await using var factory = new StoneFlowApiFactory().With(Key, value);

        Assert.Null(factory.Services.GetRequiredService<IAnonymizationSettings>().RetentionMonths);
    }

    [Theory]
    [InlineData("0", null)]
    [InlineData("-1", "-1")]
    [InlineData("1201", "1201")]
    [InlineData("abc", "abc")]
    [InlineData("12.5", "12.5")]
    [InlineData("60 m", "60 m")]
    public async Task Invalid_value_stops_the_host_without_echoing_it(string value, string? forbidden)
    {
        // „0” występuje w „1200” w komunikacie, więc dla niego nie sprawdza się braku wartości.
        await using var factory = new StoneFlowApiFactory().With(Key, value);

        StartupFailure.AssertOptionsValidation(factory, Key, forbiddenValue: forbidden);
    }

    [Theory]
    [InlineData("60", 60)]
    [InlineData(" 60 ", 60)]
    [InlineData("1", 1)]
    [InlineData("1200", 1200)]
    public async Task Valid_value_is_available_to_orders(string value, int expected)
    {
        await using var factory = new StoneFlowApiFactory().With(Key, value);

        Assert.Equal(expected, factory.Services.GetRequiredService<IAnonymizationSettings>().RetentionMonths);
    }

    [Fact]
    public void No_repository_file_sets_a_retention_period()
    {
        var root = RepositoryRoot.Path;
        var files = new List<string>
        {
            Path.Combine(root, "api", "src", "StoneFlow.Api", "Properties", "launchSettings.json"),
            Path.Combine(root, "app", "playwright.config.ts"),
        };
        files.AddRange(Directory.GetFiles(Path.Combine(root, "api", "src", "StoneFlow.Api"), "appsettings*.json"));
        files.AddRange(Directory.GetFiles(Path.Combine(root, "app", "e2e"), "*", SearchOption.AllDirectories));
        files.AddRange(Directory.GetFiles(Path.Combine(root, ".github", "workflows"), "*.yml"));

        foreach (var file in files.Where(File.Exists))
        {
            var text = File.ReadAllText(file);
            Assert.False(text.Contains("RetentionMonths", StringComparison.Ordinal), $"{file} ustawia okres retencji.");
            Assert.False(text.Contains("\"Anonymization\"", StringComparison.Ordinal), $"{file} ma sekcję anonimizacji.");
        }

        var example = File.ReadAllLines(Path.Combine(root, "deploy", "api.env.example"));
        Assert.All(
            example.Where(line => line.Contains("Anonymization", StringComparison.Ordinal)),
            line =>
            {
                Assert.StartsWith("#", line.TrimStart(), StringComparison.Ordinal);
                Assert.DoesNotMatch(NumberAfterEquals(), line);
            });
    }

    [GeneratedRegex(@"=\s*\d")]
    private static partial Regex NumberAfterEquals();
}
