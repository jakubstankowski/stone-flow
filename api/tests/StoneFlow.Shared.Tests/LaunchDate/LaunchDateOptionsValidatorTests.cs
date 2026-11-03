using StoneFlow.Shared.Infrastructure.LaunchDate;

namespace StoneFlow.Shared.Tests.LaunchDate;

public sealed class LaunchDateOptionsValidatorTests
{
    private readonly LaunchDateOptionsValidator _validator = new();

    [Fact]
    public void Accepts_iso_date()
    {
        var result = _validator.Validate(null, new LaunchDateOptions { LaunchDate = "2026-11-03" });

        Assert.True(result.Succeeded);
        Assert.True(LaunchDateOptionsValidator.TryParse("2026-11-03", out var date));
        Assert.Equal(new DateOnly(2026, 11, 3), date);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("03.11.2026")]
    [InlineData("2026-02-30")]
    [InlineData("2026-11-03T10:00")]
    [InlineData(" 2026-11-03")]
    [InlineData("2026-11-3")]
    public void Rejects_invalid(string? value)
    {
        var result = _validator.Validate(null, new LaunchDateOptions { LaunchDate = value });

        Assert.True(result.Failed);
        Assert.Contains(LaunchDateOptions.LaunchDateKey, result.FailureMessage, StringComparison.Ordinal);
    }
}
