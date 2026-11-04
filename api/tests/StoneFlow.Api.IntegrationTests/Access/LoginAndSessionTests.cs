using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using StoneFlow.Access.Api;
using StoneFlow.Api.IntegrationTests.Infrastructure;

namespace StoneFlow.Api.IntegrationTests.Access;

/// <summary>Logowanie, blokada, sesja i wylogowanie na prawdziwej bazie (DOST-01).</summary>
[Collection(SqlServerCollection.Name)]
public sealed class LoginAndSessionTests(SqlServerFixture sqlServer)
{
    [Fact]
    public async Task Owner_logs_in_and_session_returns_email()
    {
        await using var factory = await CreateAsync();
        using var client = AuthClient.Create(factory);
        await client.SetUpOwnerAsync(factory);

        using var login = await client.LoginAsync("  OWNER@Example.com ");
        using var session = await client.GetSessionAsync();

        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.Equal(StoneFlowApiFactory.OwnerEmail, (await AuthClient.ProblemAsync(session)).GetProperty("email").GetString());
    }

    [Fact]
    public async Task Wrong_password_unknown_email_and_account_without_password_give_the_same_401()
    {
        await using var factory = await CreateAsync();
        using var client = AuthClient.Create(factory);
        (await client.RequestResetAsync()).Dispose(); // konto właściciela bez hasła

        using var withoutPassword = await client.LoginAsync(password: "jakiekolwiek-haslo");
        using var unknown = await client.LoginAsync("nikt@example.com", "jakiekolwiek-haslo");

        foreach (var response in new[] { withoutPassword, unknown })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var problem = await AuthClient.ProblemAsync(response);
            Assert.Equal(AuthProblems.InvalidCredentialsDetail, problem.GetProperty("detail").GetString());
            Assert.Equal(AuthProblems.InvalidCredentialsCode, problem.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Five_failures_lock_the_account_for_15_minutes_with_the_same_message()
    {
        await using var factory = await CreateAsync();
        using var client = AuthClient.Create(factory);
        await client.SetUpOwnerAsync(factory);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await client.LoginAsync(password: "zle-haslo-numer-" + attempt)).Dispose();
        }

        using var correctWhileLocked = await client.LoginAsync();
        var lockoutEnd = await TestDatabase.QueryAsync(factory, db => db.Users.Select(u => u.LockoutEndUtc).SingleAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, correctWhileLocked.StatusCode);
        Assert.Equal(AuthProblems.InvalidCredentialsDetail, (await AuthClient.ProblemAsync(correctWhileLocked)).GetProperty("detail").GetString());
        Assert.NotNull(lockoutEnd);
        Assert.InRange(lockoutEnd.Value - DateTime.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15.5));
    }

    [Fact]
    public async Task Expired_lockout_allows_login_and_success_resets_the_counter()
    {
        await using var factory = await CreateAsync();
        using var client = AuthClient.Create(factory);
        await client.SetUpOwnerAsync(factory);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await client.LoginAsync(password: "zle-haslo-numer-" + attempt)).Dispose();
        }

        await TestDatabase.QueryAsync(factory, db => db.Users.ExecuteUpdateAsync(
            set => set.SetProperty(u => u.LockoutEndUtc, DateTime.UtcNow.AddMinutes(-1)),
            TestContext.Current.CancellationToken));
        using var login = await client.LoginAsync();
        var failedCount = await TestDatabase.QueryAsync(factory, db => db.Users.Select(u => u.AccessFailedCount).SingleAsync());

        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        Assert.Equal(0, failedCount);
    }

    [Fact]
    public async Task Development_cookies_have_no_host_prefix()
    {
        await using var factory = await CreateAsync(environment: "Development");
        using var client = AuthClient.Create(factory);
        await client.SetUpOwnerAsync(factory);
        using var fresh = AuthClient.Create(factory);

        using var csrf = await fresh.GetCsrfAsync();
        using var login = await client.LoginAsync();
        var cookies = csrf.Headers.GetValues("Set-Cookie").Concat(login.Headers.GetValues("Set-Cookie")).ToArray();

        Assert.Contains(cookies, c => c.StartsWith("sf_session=", StringComparison.Ordinal));
        Assert.Contains(cookies, c => c.StartsWith("sf_af=", StringComparison.Ordinal));
        Assert.DoesNotContain(cookies, c => c.StartsWith("__Host-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_fields_return_validation_problem()
    {
        await using var factory = await CreateAsync();
        using var client = AuthClient.Create(factory);
        (await client.GetCsrfAsync()).Dispose();

        using var response = await client.PostAsync("/api/auth/login", new { email = "", password = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await AuthClient.ProblemAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("email", out _));
        Assert.True(errors.TryGetProperty("password", out _));
    }

    [Fact]
    public async Task Logout_ends_the_session_and_invalidates_a_copied_cookie()
    {
        await using var factory = await CreateAsync();
        using var client = AuthClient.Create(factory);
        await client.SetUpOwnerAsync(factory);
        (await client.LoginAsync()).Dispose();
        var copiedCookie = client.SessionCookie;
        Assert.NotNull(copiedCookie);

        using var logout = await client.PostAsync("/api/auth/logout");
        using var afterLogout = await client.GetSessionAsync();
        using var copied = await SessionWithCookieAsync(factory, copiedCookie);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, copied.StatusCode);
    }

    [Fact]
    public async Task Production_cookies_have_secure_attributes()
    {
        await using var factory = await CreateAsync();
        using var client = AuthClient.Create(factory);
        await client.SetUpOwnerAsync(factory);
        using var freshClient = AuthClient.Create(factory);
        using var csrf = await freshClient.GetCsrfAsync();
        (await client.GetCsrfAsync()).Dispose();
        var antiforgeryCookie = csrf.Headers.GetValues("Set-Cookie").Select(c => c.ToLowerInvariant()).ToArray();

        using var login = await client.PostAsync("/api/auth/login", new { email = StoneFlowApiFactory.OwnerEmail, password = AuthClient.Password });
        var cookies = login.Headers.GetValues("Set-Cookie").Select(c => c.ToLowerInvariant()).ToArray();

        var session = Assert.Single(cookies, c => c.StartsWith("__host-sf_session=", StringComparison.Ordinal));
        Assert.Contains("secure", session, StringComparison.Ordinal);
        Assert.Contains("httponly", session, StringComparison.Ordinal);
        Assert.Contains("samesite=lax", session, StringComparison.Ordinal);
        Assert.Contains("path=/", session, StringComparison.Ordinal);
        Assert.DoesNotContain("expires=", session, StringComparison.Ordinal);
        Assert.DoesNotContain("domain=", session, StringComparison.Ordinal);

        var xsrf = Assert.Single(cookies, c => c.StartsWith("xsrf-token=", StringComparison.Ordinal));
        Assert.DoesNotContain("httponly", xsrf, StringComparison.Ordinal);
        Assert.Contains("samesite=strict", xsrf, StringComparison.Ordinal);
        Assert.Contains("secure", xsrf, StringComparison.Ordinal);
        var antiforgery = Assert.Single(antiforgeryCookie, c => c.StartsWith("__host-sf_af=", StringComparison.Ordinal));
        Assert.Contains("httponly", antiforgery, StringComparison.Ordinal);
        Assert.Contains("samesite=strict", antiforgery, StringComparison.Ordinal);
        Assert.Contains("secure", antiforgery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Session_survives_api_restart()
    {
        var database = sqlServer.NewDatabase("restart");
        string? cookie;
        await using (var first = new StoneFlowApiFactory().WithDatabase(database))
        {
            await TestDatabase.MigrateAsync(first);
            using var client = AuthClient.Create(first);
            await client.SetUpOwnerAsync(first);
            (await client.LoginAsync()).Dispose();
            cookie = client.SessionCookie;
        }

        await using var second = new StoneFlowApiFactory().WithDatabase(database);
        using var session = await SessionWithCookieAsync(second, cookie);

        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    }

    [Fact]
    public async Task Csrf_token_issued_before_login_is_rejected_afterwards()
    {
        await using var factory = await CreateAsync();
        using var client = AuthClient.Create(factory);
        await client.SetUpOwnerAsync(factory);
        (await client.GetCsrfAsync()).Dispose();
        var anonymousToken = client.XsrfToken;
        (await client.PostAsync("/api/auth/login", new { email = StoneFlowApiFactory.OwnerEmail, password = AuthClient.Password })).Dispose();

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/auth/logout", UriKind.Relative));
        request.Headers.Add("X-XSRF-TOKEN", anonymousToken);
        using var logout = await client.Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.NotEqual(anonymousToken, client.XsrfToken);
        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
        Assert.Equal(AuthProblems.CsrfInvalidCode, (await AuthClient.ProblemAsync(logout)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Expired_session_post_returns_401_not_csrf_error()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = await CreateAsync(time);
        using var client = AuthClient.Create(factory);
        await client.SetUpOwnerAsync(factory);
        (await client.LoginAsync()).Dispose();

        time.Advance(TimeSpan.FromMinutes(61));
        using var logout = await client.PostAsync("/api/auth/logout");

        Assert.Equal(HttpStatusCode.Unauthorized, logout.StatusCode);
    }

    [Fact]
    public async Task Session_expires_after_60_minutes_idle_and_activity_extends_it()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = await CreateAsync(time);
        using var client = AuthClient.Create(factory);
        await client.SetUpOwnerAsync(factory);
        (await client.LoginAsync()).Dispose();

        for (var step = 0; step < 3; step++)
        {
            time.Advance(TimeSpan.FromMinutes(50));
            using var active = await client.GetSessionAsync();
            Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        }

        time.Advance(TimeSpan.FromMinutes(61));
        using var idle = await client.GetSessionAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, idle.StatusCode);
    }

    [Fact]
    public async Task Session_ends_12_hours_after_login_despite_activity()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = await CreateAsync(time);
        using var client = AuthClient.Create(factory);
        await client.SetUpOwnerAsync(factory);
        (await client.LoginAsync()).Dispose();

        for (var elapsed = TimeSpan.Zero; elapsed < TimeSpan.FromHours(11.5); elapsed += TimeSpan.FromMinutes(30))
        {
            time.Advance(TimeSpan.FromMinutes(30));
            using var active = await client.GetSessionAsync();
            Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        }

        time.Advance(TimeSpan.FromMinutes(31));
        using var expired = await client.GetSessionAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Fact]
    public async Task Logs_contain_no_email_password_or_token()
    {
        await using var factory = await CreateAsync();
        using var client = AuthClient.Create(factory);
        await client.SetUpOwnerAsync(factory);
        var token = factory.Emails.LastLink().Token;
        (await client.LoginAsync(password: "zle-haslo-do-logu")).Dispose();
        (await client.LoginAsync()).Dispose();
        (await client.PostAsync("/api/auth/logout")).Dispose();

        var logs = factory.Logs.GetSnapshot();
        var text = string.Join('\n', logs.Select(r => r.Message + " " + r.Exception));

        Assert.Contains(logs, r => r.Id.Id == 6000);
        Assert.DoesNotContain(StoneFlowApiFactory.OwnerEmail, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AuthClient.Password, text, StringComparison.Ordinal);
        Assert.DoesNotContain("zle-haslo-do-logu", text, StringComparison.Ordinal);
        Assert.DoesNotContain(token, text, StringComparison.Ordinal);
    }

    private async Task<StoneFlowApiFactory> CreateAsync(FakeTimeProvider? time = null, string environment = "Production")
    {
        var factory = new StoneFlowApiFactory { Time = time, Environment = environment }.WithDatabase(sqlServer.NewDatabase("login"));
        await TestDatabase.MigrateAsync(factory);
        return factory;
    }

    private static async Task<HttpResponseMessage> SessionWithCookieAsync(StoneFlowApiFactory factory, string? cookie)
    {
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            BaseAddress = new Uri("https://localhost"),
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/auth/session", UriKind.Relative));
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
