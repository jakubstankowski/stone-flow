using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using StoneFlow.Api.IntegrationTests.Infrastructure;

namespace StoneFlow.Api.IntegrationTests.Security;

/// <summary>
/// HTTPS poza Development: HSTS i przekierowanie HTTP → HTTPS. Host inny niż <c>localhost</c>,
/// bo HSTS domyślnie pomija hosty lokalne.
/// </summary>
public sealed class HttpsTests
{
    private static readonly Uri Health = new("/health", UriKind.Relative);

    [Fact]
    public async Task Production_sends_hsts_over_https()
    {
        await using var factory = new StoneFlowApiFactory();
        using var client = CreateClient(factory, "https://api.example.com");

        using var response = await client.GetAsync(Health, TestContext.Current.CancellationToken);

        Assert.True(response.Headers.TryGetValues("Strict-Transport-Security", out var values));
        Assert.Equal("max-age=31536000", Assert.Single(values));
    }

    [Fact]
    public async Task Production_redirects_http_to_https()
    {
        await using var factory = new StoneFlowApiFactory();
        using var client = CreateClient(factory, "http://api.example.com");

        using var response = await client.GetAsync(Health, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.PermanentRedirect, response.StatusCode);
        Assert.Equal(new Uri("https://api.example.com/health"), response.Headers.Location);
    }

    [Fact]
    public async Task Development_keeps_plain_http_without_hsts()
    {
        await using var factory = new StoneFlowApiFactory { Environment = "Development" };
        using var client = CreateClient(factory, "http://api.example.com");

        using var response = await client.GetAsync(Health, TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.PermanentRedirect, response.StatusCode);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    private static HttpClient CreateClient(StoneFlowApiFactory factory, string baseAddress) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(baseAddress),
            AllowAutoRedirect = false,
        });
}
