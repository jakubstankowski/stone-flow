using System.Net;
using StoneFlow.Api.IntegrationTests.Infrastructure;

namespace StoneFlow.Api.IntegrationTests;

public sealed class OpenApiTests
{
    private static readonly Uri DocumentUri = new("/openapi/v1.json", UriKind.Relative);

    [Fact]
    public async Task Document_available_in_development()
    {
        await using var factory = new StoneFlowApiFactory { Environment = "Development" };
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(DocumentUri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"openapi\"", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Not_exposed_outside_development()
    {
        await using var factory = new StoneFlowApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestRequestStartupFilter.AccountHeader, Guid.NewGuid().ToString("D"));

        using var response = await client.GetAsync(DocumentUri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
