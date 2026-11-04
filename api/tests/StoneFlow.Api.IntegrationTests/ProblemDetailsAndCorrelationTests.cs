using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using StoneFlow.Api.IntegrationTests.Infrastructure;

namespace StoneFlow.Api.IntegrationTests;

public sealed class ProblemDetailsAndCorrelationTests
{
    private const string CorrelationHeader = "X-Correlation-ID";

    [Fact]
    public async Task Not_found_returns_problem_details_with_trace_id()
    {
        await using var factory = new StoneFlowApiFactory();
        using var client = AuthenticatedClient(factory);

        using var response = await client.GetAsync(new Uri("/nie-ma", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadProblem(response);
        Assert.Equal(CorrelationId(response), problem.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task Unhandled_exception_returns_problem_details()
    {
        await using var factory = new StoneFlowApiFactory();
        using var client = AuthenticatedClient(factory);

        using var response = await client.GetAsync(new Uri(ThrowingEndpointStartupFilter.Path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadProblem(response);
        Assert.Equal(500, problem.GetProperty("status").GetInt32());
        Assert.Equal(CorrelationId(response), problem.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task Production_error_has_no_exception_details()
    {
        await using var factory = new StoneFlowApiFactory();
        using var client = AuthenticatedClient(factory);

        using var response = await client.GetAsync(new Uri(ThrowingEndpointStartupFilter.Path, UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(ThrowingEndpointStartupFilter.ExceptionMessage, body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
        Assert.False(JsonDocument.Parse(body).RootElement.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task Traceparent_is_propagated()
    {
        const string traceId = "0af7651916cd43dd8448eb211c80319c";
        await using var factory = new StoneFlowApiFactory();
        using var client = AuthenticatedClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/nie-ma", UriKind.Relative));
        request.Headers.Add("traceparent", $"00-{traceId}-b7ad6b7169203331-01");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(traceId, CorrelationId(response));
        Assert.Equal(traceId, (await ReadProblem(response)).GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task Incoming_correlation_header_is_ignored()
    {
        await using var factory = new StoneFlowApiFactory();
        using var client = AuthenticatedClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/nie-ma", UriKind.Relative));
        request.Headers.Add(CorrelationHeader, "podany-przez-klienta");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        var correlationId = CorrelationId(response);
        Assert.NotEqual("podany-przez-klienta", correlationId);
        Assert.Matches("^[0-9a-f]{32}$", correlationId);
    }

    [Fact]
    public async Task Log_scope_contains_correlation_id()
    {
        await using var factory = new StoneFlowApiFactory();
        using var client = AuthenticatedClient(factory);

        using var response = await client.GetAsync(new Uri(ThrowingEndpointStartupFilter.Path, UriKind.Relative), TestContext.Current.CancellationToken);

        var correlationId = CorrelationId(response);
        var collector = factory.Services.GetRequiredService<FakeLogCollector>();
        var errorLog = Assert.Single(collector.GetSnapshot(), record => record.Exception?.Message == ThrowingEndpointStartupFilter.ExceptionMessage);
        var scopeValues = errorLog.Scopes
            .OfType<IEnumerable<KeyValuePair<string, object>>>()
            .SelectMany(scope => scope);
        Assert.Contains(scopeValues, pair => pair.Key == "CorrelationId" && Equals(pair.Value, correlationId));
    }

    [Fact]
    public async Task Anonymous_unknown_path_returns_401_problem_details_with_trace_id()
    {
        await using var factory = new StoneFlowApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/nie-ma", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(CorrelationId(response), (await ReadProblem(response)).GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task Client_error_keeps_its_detail_in_production()
    {
        await using var factory = new StoneFlowApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(new Uri("/api/auth/login", UriKind.Relative), content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(StoneFlow.Access.Api.AuthProblems.CsrfInvalidDetail, (await ReadProblem(response)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Unavailable_database_returns_503_without_detail()
    {
        await using var factory = new StoneFlowApiFactory().WithDatabase(StoneFlowApiFactory.UnreachableConnectionString);
        using var client = AuthenticatedClient(factory);

        using var response = await client.GetAsync(new Uri("/api/auth/session", UriKind.Relative), TestContext.Current.CancellationToken);
        var problem = await ReadProblem(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(problem.TryGetProperty("detail", out _));
        Assert.DoesNotContain("127.0.0.1", problem.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Klient z testowym użytkownikiem (filtr testowy), aby żądanie przeszło politykę sesji.</summary>
    private static HttpClient AuthenticatedClient(StoneFlowApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestRequestStartupFilter.AccountHeader, Guid.NewGuid().ToString("D"));
        return client;
    }

    private static string CorrelationId(HttpResponseMessage response) =>
        Assert.Single(response.Headers.GetValues(CorrelationHeader));

    private static async Task<JsonElement> ReadProblem(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(body).RootElement.Clone();
    }
}
