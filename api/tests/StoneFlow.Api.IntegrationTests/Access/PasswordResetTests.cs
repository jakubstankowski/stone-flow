using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using StoneFlow.Access.Api;
using StoneFlow.Access.Application.PasswordReset;
using StoneFlow.Api.IntegrationTests.Infrastructure;

namespace StoneFlow.Api.IntegrationTests.Access;

/// <summary>Reset hasła i utworzenie konta właściciela.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class PasswordResetTests(SqlServerFixture sqlServer)
{
    [Fact]
    public async Task First_reset_for_owner_creates_account_without_password_and_sends_fragment_link()
    {
        await using var factory = await CreateAsync();
        using var client = AuthClient.Create(factory);

        using var response = await client.RequestResetAsync(" Owner@Example.com ");
        var users = await TestDatabase.QueryAsync(factory, db => db.Users.ToListAsync());
        var message = Assert.Single(factory.Emails.Messages);
        var link = factory.Emails.LastLink();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var user = Assert.Single(users);
        Assert.Null(user.PasswordHash);
        Assert.Equal(StoneFlowApiFactory.OwnerEmail, message.To);
        Assert.StartsWith(StoneFlowApiFactory.AppBaseUrl + "/reset-hasla/nowe#konto=", link.Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Empty(link.Uri.Query);
        Assert.DoesNotContain("owner", link.Uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(user.Id.ToString("D"), link.AccountId);
    }

    [Fact]
    public async Task Other_email_never_creates_account_and_response_is_the_same()
    {
        await using var factory = await CreateAsync();
        using var client = AuthClient.Create(factory);

        using var response = await client.RequestResetAsync("ktos@example.com");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(0, await TestDatabase.QueryAsync(factory, db => db.Users.CountAsync()));
        Assert.Empty(factory.Emails.Messages);
    }

    [Fact]
    public async Task Changed_owner_email_does_not_create_a_second_account()
    {
        var database = sqlServer.NewDatabase("owner");
        await using (var first = new StoneFlowApiFactory().WithDatabase(database))
        {
            await TestDatabase.MigrateAsync(first);
            using var client = AuthClient.Create(first);
            (await client.RequestResetAsync()).Dispose();
        }

        await using var changed = new StoneFlowApiFactory().WithDatabase(database).With("StoneFlow:Owner:Email", "nowy@example.com");
        using var second = AuthClient.Create(changed);
        using var response = await second.RequestResetAsync("nowy@example.com");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, await TestDatabase.QueryAsync(changed, db => db.Users.CountAsync()));
        Assert.Empty(changed.Emails.Messages);
    }

    [Fact]
    public async Task Concurrent_first_requests_create_one_account()
    {
        await using var factory = await CreateAsync();
        var clients = Enumerable.Range(0, 3).Select(_ => AuthClient.Create(factory)).ToArray();

        var responses = await Task.WhenAll(clients.Select(c => c.RequestResetAsync()));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        Assert.Equal(1, await TestDatabase.QueryAsync(factory, db => db.Users.CountAsync()));
        foreach (var disposable in responses.Cast<IDisposable>().Concat(clients))
        {
            disposable.Dispose();
        }
    }

    [Fact]
    public async Task Link_works_once_and_database_stores_only_the_hash()
    {
        await using var factory = await CreateAsync();
        using var client = AuthClient.Create(factory);
        (await client.RequestResetAsync()).Dispose();
        var link = factory.Emails.LastLink();

        using var first = await client.ConfirmResetAsync(link.AccountId, link.Token);
        using var second = await client.ConfirmResetAsync(link.AccountId, link.Token, "inne-dobre-haslo-2");
        var stored = await TestDatabase.QueryAsync(factory, db => db.PasswordResetTokens.Select(t => t.TokenHash).SingleAsync());

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal(AuthProblems.ResetLinkInvalidCode, (await AuthClient.ProblemAsync(second)).GetProperty("code").GetString());
        Assert.Equal(ResetToken.Hash(link.Token), stored);
    }

    [Fact]
    public async Task Link_expires_after_30_minutes()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = await CreateAsync(time);
        using var client = AuthClient.Create(factory);
        (await client.RequestResetAsync()).Dispose();
        var link = factory.Emails.LastLink();

        time.Advance(TimeSpan.FromMinutes(31));
        using var response = await client.ConfirmResetAsync(link.AccountId, link.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AuthProblems.ResetLinkInvalidDetail, (await AuthClient.ProblemAsync(response)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Second_request_revokes_the_first_link()
    {
        await using var factory = await CreateAsync();
        using var client = AuthClient.Create(factory);
        (await client.RequestResetAsync()).Dispose();
        var firstLink = factory.Emails.LastLink();
        (await client.RequestResetAsync()).Dispose();
        var secondLink = factory.Emails.LastLink();

        using var first = await client.ConfirmResetAsync(firstLink.AccountId, firstLink.Token);
        using var second = await client.ConfirmResetAsync(secondLink.AccountId, secondLink.Token);

        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
    }

    [Fact]
    public async Task Tampered_token_or_account_returns_the_same_error()
    {
        await using var factory = await CreateAsync();
        using var client = AuthClient.Create(factory);
        (await client.RequestResetAsync()).Dispose();
        var link = factory.Emails.LastLink();

        using var wrongToken = await client.ConfirmResetAsync(link.AccountId, ResetToken.Generate());
        using var wrongAccount = await client.ConfirmResetAsync(Guid.NewGuid().ToString("D"), link.Token);
        using var garbage = await client.ConfirmResetAsync("nie-guid", "abc");

        foreach (var response in new[] { wrongToken, wrongAccount, garbage })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(AuthProblems.ResetLinkInvalidCode, (await AuthClient.ProblemAsync(response)).GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Weak_password_returns_field_error_and_keeps_the_link_valid()
    {
        await using var factory = await CreateAsync();
        using var client = AuthClient.Create(factory);
        (await client.RequestResetAsync()).Dispose();
        var link = factory.Emails.LastLink();

        using var tooShort = await client.ConfirmResetAsync(link.AccountId, link.Token, "krotkie");
        using var sameAsEmail = await client.ConfirmResetAsync(link.AccountId, link.Token, "OWNER@example.com");
        using var valid = await client.ConfirmResetAsync(link.AccountId, link.Token);

        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.True((await AuthClient.ProblemAsync(tooShort)).GetProperty("errors").TryGetProperty("newPassword", out _));
        Assert.Equal(HttpStatusCode.BadRequest, sameAsEmail.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);
    }

    [Fact]
    public async Task Concurrent_confirms_succeed_only_once()
    {
        await using var factory = await CreateAsync();
        using var client = AuthClient.Create(factory);
        (await client.RequestResetAsync()).Dispose();
        var link = factory.Emails.LastLink();
        var clients = Enumerable.Range(0, 4).Select(_ => AuthClient.Create(factory)).ToArray();

        var responses = await Task.WhenAll(clients.Select(c => c.ConfirmResetAsync(link.AccountId, link.Token)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest));
        foreach (var disposable in responses.Cast<IDisposable>().Concat(clients))
        {
            disposable.Dispose();
        }
    }

    [Fact]
    public async Task Reset_invalidates_existing_sessions_and_unlocks_the_account()
    {
        await using var factory = await CreateAsync();
        using var browser = AuthClient.Create(factory);
        await browser.SetUpOwnerAsync(factory);
        (await browser.LoginAsync()).Dispose();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var other = AuthClient.Create(factory);
            (await other.LoginAsync(password: "zle-haslo-" + attempt)).Dispose();
        }

        using var resetClient = AuthClient.Create(factory);
        (await resetClient.RequestResetAsync()).Dispose();
        var link = factory.Emails.LastLink();
        using var confirm = await resetClient.ConfirmResetAsync(link.AccountId, link.Token, "zupelnie-nowe-haslo");
        await Task.Delay(10, TestContext.Current.CancellationToken);
        using var oldSession = await browser.GetSessionAsync();
        using var noSession = await resetClient.GetSessionAsync();
        using var login = await resetClient.LoginAsync(password: "zupelnie-nowe-haslo");

        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, noSession.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    [Fact]
    public async Task Fourth_request_for_account_in_15_minutes_sends_no_email()
    {
        await using var factory = await CreateAsync();
        for (var request = 0; request < 4; request++)
        {
            using var client = new AuthClient(factory.CreateClient());
            client.Http.DefaultRequestHeaders.Add(TestRequestStartupFilter.RemoteIpHeader, $"10.0.0.{request + 1}");
            using var response = await client.RequestResetAsync();
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        Assert.Equal(3, factory.Emails.Messages.Count);
    }

    [Fact]
    public async Task Smtp_failure_still_returns_202_and_logs_without_content()
    {
        await using var factory = new StoneFlowApiFactory { UseRealEmailSender = true }
            .WithDatabase(sqlServer.NewDatabase("smtp"))
            .With("StoneFlow:Email:Smtp:Host", "127.0.0.1")
            .With("StoneFlow:Email:Smtp:Port", "1");
        await TestDatabase.MigrateAsync(factory);
        using var client = AuthClient.Create(factory);

        using var response = await client.RequestResetAsync();
        var text = string.Join('\n', factory.Logs.GetSnapshot().Select(r => r.Message + " " + r.Exception));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains(factory.Logs.GetSnapshot(), r => r.Id.Id == 6012);
        Assert.DoesNotContain("reset-hasla", text, StringComparison.Ordinal);
        Assert.DoesNotContain(StoneFlowApiFactory.OwnerEmail, text, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<StoneFlowApiFactory> CreateAsync(FakeTimeProvider? time = null)
    {
        var factory = new StoneFlowApiFactory { Time = time }.WithDatabase(sqlServer.NewDatabase("reset"));
        await TestDatabase.MigrateAsync(factory);
        return factory;
    }
}
