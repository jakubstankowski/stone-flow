using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using StoneFlow.Api.IntegrationTests.Infrastructure;

namespace StoneFlow.Api.IntegrationTests.Scheduling;

/// <summary>SF-11: <c>GET /api/jobs</c> — stan zadań tylko dla zalogowanego właściciela.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class JobsEndpointTests(SqlServerFixture sqlServer)
{
    private static readonly Uri Jobs = new("/api/jobs", UriKind.Relative);

    [Fact]
    public async Task Requires_session()
    {
        await using var factory = new StoneFlowApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Jobs, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Without_registered_jobs_returns_empty_list_without_database()
    {
        await using var factory = new StoneFlowApiFactory();
        using var client = factory.CreateClient();

        using var response = await GetAsOwnerAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Shows_last_failed_run_and_backoff()
    {
        var time = new FakeTimeProvider(SchedulerHost.Start);
        var control = new TestJobControl { Behavior = (_, _) => throw new InvalidOperationException("Sklep nie odpowiada.") };
        await using var host = await SchedulerHost.CreateAsync(sqlServer.NewDatabase("jobsapi"), time, control);
        await host.CheckAsync();
        time.SetUtcNow(SchedulerHost.Slot + TimeSpan.FromMinutes(10));
        await host.CheckAsync();
        using var client = host.Factory.CreateClient();

        using var response = await GetAsOwnerAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var job = Assert.Single(json.RootElement.EnumerateArray().ToList());
        Assert.Equal(TestJob.Key, job.GetProperty("key").GetString());
        Assert.Equal("daily 23:00 Europe/Warsaw", job.GetProperty("schedule").GetString());
        Assert.False(job.GetProperty("isRunning").GetBoolean());
        Assert.Equal(1, job.GetProperty("consecutiveFailures").GetInt32());
        Assert.Equal(JsonValueKind.Null, job.GetProperty("lastSuccessStartedAtUtc").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, job.GetProperty("nextAttemptAtUtc").ValueKind);
        var lastRun = job.GetProperty("lastRun");
        Assert.Equal("failed", lastRun.GetProperty("status").GetString());
        Assert.Equal("InvalidOperationException: Sklep nie odpowiada.", lastRun.GetProperty("error").GetString());
    }

    private static Task<HttpResponseMessage> GetAsOwnerAsync(HttpClient client)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Jobs);
        request.Headers.Add(TestRequestStartupFilter.AccountHeader, Guid.NewGuid().ToString());
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
