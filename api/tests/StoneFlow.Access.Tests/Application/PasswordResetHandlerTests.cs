using Microsoft.Extensions.Time.Testing;
using StoneFlow.Access.Application;
using StoneFlow.Access.Application.Abstractions;
using StoneFlow.Access.Application.PasswordReset;
using StoneFlow.Access.Domain.PasswordReset;

namespace StoneFlow.Access.Tests.Application;

public sealed class ResetTokenAndLinkTests
{
    [Fact]
    public void Token_has_256_bits_and_is_url_safe()
    {
        var token = ResetToken.Generate();

        Assert.Equal(ResetToken.EncodedLength, token.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", token);
        Assert.NotNull(ResetToken.TryHash(token));
        Assert.Equal(32, ResetToken.Hash(token).Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("za-krotki")]
    [InlineData("+++++++++++++++++++++++++++++++++++++++++++")]
    public void TryHash_rejects_malformed_tokens(string? token) => Assert.Null(ResetToken.TryHash(token));

    [Fact]
    public void Link_puts_account_and_token_in_the_fragment_without_email()
    {
        var accountId = Guid.NewGuid();

        var link = PasswordResetEmail.Link(new Uri("https://app.example.com/"), accountId, "abc");

        Assert.Equal($"https://app.example.com/reset-hasla/nowe#konto={accountId:D}&token=abc", link.AbsoluteUri);
        Assert.Empty(link.Query);
    }

    [Fact]
    public void Email_contains_link_and_validity_without_shop_identifiers()
    {
        var link = new Uri("https://app.example.com/reset-hasla/nowe#konto=1&token=2");

        var message = PasswordResetEmail.Create("owner@example.com", link);

        Assert.Contains(link.AbsoluteUri, message.TextBody, StringComparison.Ordinal);
        Assert.Contains("30 minut", message.TextBody, StringComparison.Ordinal);
        Assert.StartsWith("Stone Flow", message.Subject, StringComparison.Ordinal);
    }
}

public sealed class RequestPasswordResetHandlerTests
{
    private readonly FakeAccounts _accounts = new();
    private readonly FakeTokens _tokens = new();
    private readonly FakeEmail _email = new();
    private readonly FakeEvents _events = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 4, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Creates_account_only_for_owner_when_none_exists()
    {
        var result = await Handle(" OWNER@example.com ");

        Assert.True(result.Accepted);
        Assert.Single(_accounts.All);
        Assert.Single(_email.Sent);
        Assert.Single(_tokens.Issued);
    }

    [Fact]
    public async Task Other_email_never_creates_account()
    {
        var result = await Handle("ktos@example.com");

        Assert.True(result.Accepted);
        Assert.Empty(_accounts.All);
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task Owner_email_is_ignored_when_an_account_already_exists()
    {
        _accounts.Add("poprzedni@example.com");

        await Handle("owner@example.com");

        Assert.Single(_accounts.All);
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task Fourth_request_in_15_minutes_sends_no_email()
    {
        for (var request = 0; request < 4; request++)
        {
            Assert.True((await Handle("owner@example.com")).Accepted);
        }

        Assert.Equal(3, _email.Sent.Count);
        Assert.Contains(nameof(ISecurityEvents.ResetSuppressedByAccountLimit), _events.Names);
    }

    [Fact]
    public async Task Smtp_failure_is_still_accepted_and_logged()
    {
        _email.Result = EmailDeliveryResult.Failed;

        var result = await Handle("owner@example.com");

        Assert.True(result.Accepted);
        Assert.Contains(nameof(ISecurityEvents.ResetEmailFailed), _events.Names);
    }

    [Theory]
    [InlineData("", AccessMessages.EmailRequired)]
    [InlineData("nie-adres", AccessMessages.EmailInvalid)]
    public async Task Invalid_email_returns_validation_error(string email, string message)
    {
        var result = await Handle(email);

        Assert.False(result.Accepted);
        Assert.Equal([message], result.Errors!.ToDictionary()[AccessMessages.EmailField]);
    }

    private Task<RequestPasswordResetResult> Handle(string email) =>
        new RequestPasswordResetHandler(
                _accounts,
                _tokens,
                new ImmediateUnitOfWork(),
                _email,
                _events,
                new AccessSettings("owner@example.com", new Uri("https://app.example.com")),
                _time)
            .HandleAsync(new RequestPasswordResetCommand(email), CancellationToken.None);
}

public sealed class ConfirmPasswordResetHandlerTests
{
    private readonly FakeAccounts _accounts = new();
    private readonly FakeTokens _tokens = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 4, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Weak_password_does_not_consume_the_token()
    {
        var (accountId, token) = Issue();

        var result = await Handle(accountId.ToString("D"), token, "krotkie");

        Assert.Equal(ConfirmPasswordResetOutcome.ValidationFailed, result.Outcome);
        Assert.Equal(0, _tokens.ConsumeCalls);
        Assert.Null(_accounts.All.Single().Password);
    }

    [Fact]
    public async Task Invalid_account_id_or_token_returns_invalid_link()
    {
        var (accountId, _) = Issue();

        Assert.Equal(ConfirmPasswordResetOutcome.InvalidLink, (await Handle("nie-guid", ResetToken.Generate(), "dobre-haslo-12")).Outcome);
        Assert.Equal(ConfirmPasswordResetOutcome.InvalidLink, (await Handle(accountId.ToString("D"), "zly", "dobre-haslo-12")).Outcome);
        Assert.Equal(ConfirmPasswordResetOutcome.InvalidLink, (await Handle(Guid.NewGuid().ToString("D"), ResetToken.Generate(), "dobre-haslo-12")).Outcome);
    }

    [Fact]
    public async Task Consumed_token_returns_invalid_link()
    {
        var (accountId, token) = Issue();

        var first = await Handle(accountId.ToString("D"), token, "dobre-haslo-12");
        var second = await Handle(accountId.ToString("D"), token, "inne-haslo-123");

        Assert.Equal(ConfirmPasswordResetOutcome.Succeeded, first.Outcome);
        Assert.Equal(ConfirmPasswordResetOutcome.InvalidLink, second.Outcome);
        Assert.Equal("dobre-haslo-12", _accounts.All.Single().Password);
    }

    private (Guid AccountId, string Token) Issue()
    {
        var account = _accounts.Add("owner@example.com");
        var token = ResetToken.Generate();
        _tokens.Issued.Add(PasswordResetToken.Issue(account.Id, ResetToken.Hash(token), _time.GetUtcNow()));
        return (account.Id, token);
    }

    private Task<ConfirmPasswordResetResult> Handle(string accountId, string token, string password) =>
        new ConfirmPasswordResetHandler(_accounts, _tokens, new ImmediateUnitOfWork(), new FakeEvents(), _time)
            .HandleAsync(new ConfirmPasswordResetCommand(accountId, token, password), CancellationToken.None);
}
