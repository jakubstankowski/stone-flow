using StoneFlow.Access.Domain.Accounts;
using StoneFlow.Access.Domain.PasswordReset;
using StoneFlow.Access.Domain.Sessions;

namespace StoneFlow.Access.Tests.Domain;

public sealed class EmailAddressTests
{
    [Fact]
    public void Normalize_trims_and_uppercases() =>
        Assert.Equal("OWNER@EXAMPLE.COM", EmailAddress.Normalize("  Owner@Example.com "));

    [Theory]
    [InlineData("owner@example.com", true)]
    [InlineData(" owner@example.com ", true)]
    [InlineData("Owner <owner@example.com>", false)]
    [InlineData("owner", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_accepts_only_plain_addresses(string? email, bool expected) =>
        Assert.Equal(expected, EmailAddress.IsValid(email));
}

public sealed class PasswordPolicyTests
{
    [Fact]
    public void Rejects_shorter_than_12() =>
        Assert.Equal(PasswordViolation.TooShort, PasswordPolicy.Check("krotkie-11z", "owner@example.com"));

    [Fact]
    public void Accepts_12_characters_without_composition_rules() =>
        Assert.Null(PasswordPolicy.Check("aaaaaaaaaaaa", "owner@example.com"));

    [Fact]
    public void Rejects_password_equal_to_email_ignoring_case() =>
        Assert.Equal(PasswordViolation.SameAsEmail, PasswordPolicy.Check("OWNER@example.com", "owner@example.com"));

    [Fact]
    public void Rejects_missing_password() =>
        Assert.Equal(PasswordViolation.Missing, PasswordPolicy.Check(null, "owner@example.com"));
}

public sealed class SessionPolicyTests
{
    private static readonly DateTimeOffset Login = new(2026, 11, 4, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Within_12_hours_is_valid() =>
        Assert.True(SessionPolicy.IsWithinAbsoluteLifetime(Login, Login.AddHours(11).AddMinutes(59)));

    [Fact]
    public void After_12_hours_is_expired() =>
        Assert.False(SessionPolicy.IsWithinAbsoluteLifetime(Login, Login.AddHours(12)));

    [Fact]
    public void Login_time_in_the_future_is_rejected() =>
        Assert.False(SessionPolicy.IsWithinAbsoluteLifetime(Login.AddMinutes(1), Login));
}

public sealed class PasswordResetTokenTests
{
    [Fact]
    public void Issue_sets_expiry_30_minutes_after_creation()
    {
        var now = new DateTimeOffset(2026, 11, 4, 8, 0, 0, TimeSpan.Zero);

        var token = PasswordResetToken.Issue(Guid.NewGuid(), new byte[32], now);

        Assert.Equal(now.UtcDateTime, token.CreatedAtUtc);
        Assert.Equal(now.UtcDateTime.AddMinutes(30), token.ExpiresAtUtc);
        Assert.Null(token.ConsumedAtUtc);
        Assert.Null(token.RevokedAtUtc);
    }

    [Fact]
    public void Issue_rejects_hash_of_wrong_length() =>
        Assert.Throws<ArgumentException>(() => PasswordResetToken.Issue(Guid.NewGuid(), new byte[16], DateTimeOffset.UtcNow));
}
