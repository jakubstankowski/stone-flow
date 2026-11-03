using System.Net;
using StoneFlow.Api.IntegrationTests.Infrastructure;

namespace StoneFlow.Api.IntegrationTests;

/// <summary>Health endpoint bez bazy danych: niedostępna baza i brak konfiguracji.</summary>
public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Unhealthy_when_database_unavailable()
    {
        await using var factory = new StoneFlowApiFactory()
            .With("ConnectionStrings:StoneFlow", StoneFlowApiFactory.UnreachableConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unhealthy_when_connection_string_missing()
    {
        await using var factory = new StoneFlowApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Unhealthy_response_has_no_details()
    {
        await using var factory = new StoneFlowApiFactory()
            .With("ConnectionStrings:StoneFlow", StoneFlowApiFactory.UnreachableConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Unhealthy", body);
        Assert.DoesNotContain("127.0.0.1", body, StringComparison.Ordinal);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Is_anonymous()
    {
        await using var factory = new StoneFlowApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
