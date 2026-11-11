using StoneFlow.Reporting.Domain.CashFlow;
using StoneFlow.Reporting.Domain.Periods;

namespace StoneFlow.Reporting.Tests.Periods;

public sealed class LaunchCutoffTests
{
    private static readonly DateOnly Launch = new(2026, 11, 3);

    [Theory]
    [InlineData("2026-11-01", "2026-11-03")] // przed uruchomieniem
    [InlineData("2026-11-03", "2026-11-03")] // w dniu uruchomienia
    [InlineData("2026-11-10", "2026-11-10")] // po uruchomieniu
    public void Effective_from_is_the_later_of_from_and_launch_date(string from, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), LaunchCutoff.EffectiveFrom(DateOnly.Parse(from), Launch));
    }

    [Fact]
    public void Cash_flow_policy_delegates_to_the_shared_rule()
    {
        var from = new DateOnly(2026, 10, 1);

        Assert.Equal(LaunchCutoff.EffectiveFrom(from, Launch), CashFlowPolicy.EffectiveFrom(from, Launch));
    }
}
