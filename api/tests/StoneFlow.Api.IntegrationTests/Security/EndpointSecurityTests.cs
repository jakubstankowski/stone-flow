using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Access.Api;
using StoneFlow.Api.IntegrationTests.Infrastructure;

namespace StoneFlow.Api.IntegrationTests.Security;

/// <summary>
/// DOST-01 i niezmiennik 3: bez sesji API odmawia każdego odczytu i zapisu poza jawną listą
/// wyjątków; CSRF dla każdego żądania zmieniającego stan; CORS bez dozwolonych originów.
/// </summary>
public sealed class EndpointSecurityTests
{
    private static readonly string[] AllowedAnonymous =
    [
        "GET /api/auth/csrf",
        "GET /health",
        "POST /api/auth/login",
        "POST /api/auth/password-reset/confirm",
        "POST /api/auth/password-reset/request",
    ];

    [Fact]
    public async Task Anonymous_endpoints_match_the_allowed_list()
    {
        await using var factory = new StoneFlowApiFactory();
        factory.CreateClient().Dispose();

        Assert.Equal(AllowedAnonymous, AnonymousEndpoints(factory));
    }

    [Fact]
    public async Task Development_adds_only_the_openapi_document()
    {
        await using var factory = new StoneFlowApiFactory { Environment = "Development" };
        factory.CreateClient().Dispose();

        string[] expected = [.. AllowedAnonymous.Append("GET /openapi/{documentName}.json").Order(StringComparer.Ordinal)];

        Assert.Equal(expected, AnonymousEndpoints(factory));
    }

    [Fact]
    public async Task Every_protected_endpoint_returns_401_without_session()
    {
        await using var factory = new StoneFlowApiFactory();
        using var client = factory.CreateClient();
        var protectedEndpoints = Endpoints(factory).Where(e => !e.Anonymous).ToArray();

        Assert.NotEmpty(protectedEndpoints);
        foreach (var endpoint in protectedEndpoints)
        {
            using var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), new Uri(endpoint.Route, UriKind.Relative));
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{endpoint.Method} {endpoint.Route} → {response.StatusCode}");
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Unknown_path_without_session_returns_401()
    {
        await using var factory = new StoneFlowApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/nie-ma", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Every_state_changing_api_endpoint_rejects_a_missing_csrf_token()
    {
        await using var factory = new StoneFlowApiFactory();
        using var client = factory.CreateClient();
        var stateChanging = Endpoints(factory)
            .Where(e => e.Route.StartsWith("/api/", StringComparison.Ordinal) && e.Method is "POST" or "PUT" or "PATCH" or "DELETE")
            .ToArray();

        Assert.NotEmpty(stateChanging);
        foreach (var endpoint in stateChanging)
        {
            using var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), new Uri(endpoint.Route, UriKind.Relative));
            request.Headers.Add(TestRequestStartupFilter.AccountHeader, Guid.NewGuid().ToString("D"));
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{endpoint.Method} {endpoint.Route} → {response.StatusCode}");
            var problem = await AuthClient.ProblemAsync(response);
            Assert.Equal(AuthProblems.CsrfInvalidCode, problem.GetProperty("code").GetString());
            Assert.Equal(AuthProblems.CsrfInvalidDetail, problem.GetProperty("detail").GetString());
        }
    }

    [Fact]
    public async Task Unknown_api_post_is_also_csrf_validated()
    {
        await using var factory = new StoneFlowApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/nie-ma", UriKind.Relative));
        request.Headers.Add(TestRequestStartupFilter.AccountHeader, Guid.NewGuid().ToString("D"));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AuthProblems.CsrfInvalidCode, (await AuthClient.ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Cross_origin_requests_get_no_cors_headers()
    {
        await using var factory = new StoneFlowApiFactory();
        using var client = factory.CreateClient();
        using var get = new HttpRequestMessage(HttpMethod.Get, new Uri("/health", UriKind.Relative));
        get.Headers.Add("Origin", "https://evil.example");
        using var preflight = new HttpRequestMessage(HttpMethod.Options, new Uri("/api/auth/login", UriKind.Relative));
        preflight.Headers.Add("Origin", "https://evil.example");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");

        using var getResponse = await client.SendAsync(get, TestContext.Current.CancellationToken);
        using var preflightResponse = await client.SendAsync(preflight, TestContext.Current.CancellationToken);

        Assert.False(getResponse.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(preflightResponse.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(preflightResponse.Headers.Contains("Access-Control-Allow-Methods"));
    }

    [Fact]
    public async Task Eleventh_login_attempt_in_a_minute_from_one_ip_returns_429()
    {
        await using var factory = new StoneFlowApiFactory();
        using var client = factory.CreateClient();

        HttpStatusCode last = default;
        for (var attempt = 0; attempt < 11; attempt++)
        {
            using var response = await SendFromAsync(client, "/api/auth/login", "10.1.1.1");
            last = response.StatusCode;
            if (attempt < 10)
            {
                Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
            }
            else
            {
                Assert.Equal(AuthProblems.RateLimitedCode, (await AuthClient.ProblemAsync(response)).GetProperty("code").GetString());
            }
        }

        using var otherIp = await SendFromAsync(client, "/api/auth/login", "10.1.1.2");

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, otherIp.StatusCode);
    }

    [Fact]
    public async Task Fourth_reset_request_from_one_ip_returns_429()
    {
        await using var factory = new StoneFlowApiFactory();
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var allowed = await SendFromAsync(client, "/api/auth/password-reset/request", "10.2.2.2");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        using var limited = await SendFromAsync(client, "/api/auth/password-reset/request", "10.2.2.2");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendFromAsync(HttpClient client, string path, string ip)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
        request.Headers.Add(TestRequestStartupFilter.RemoteIpHeader, ip);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string[] AnonymousEndpoints(StoneFlowApiFactory factory) =>
    [
        .. Endpoints(factory).Where(e => e.Anonymous).Select(e => $"{e.Method} {e.Route}").Distinct().Order(StringComparer.Ordinal),
    ];

    private static EndpointInfo[] Endpoints(StoneFlowApiFactory factory) =>
    [
        .. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => new EndpointInfo(
                    method,
                    "/" + endpoint.RoutePattern.RawText?.TrimStart('/'),
                    endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null))),
    ];

    private sealed record EndpointInfo(string Method, string Route, bool Anonymous);
}
