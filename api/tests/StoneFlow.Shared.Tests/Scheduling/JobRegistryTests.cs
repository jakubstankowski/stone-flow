using StoneFlow.Shared.Application.Scheduling;
using StoneFlow.Shared.Infrastructure.Scheduling;

namespace StoneFlow.Shared.Tests.Scheduling;

public sealed class JobRegistryTests
{
    private static readonly JobSchedule Daily = JobSchedule.DailyAt(new TimeOnly(23, 0));

    [Fact]
    public void Duplicate_keys_are_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new JobRegistry([new JobDefinition("sync", Daily), new JobDefinition("sync", Daily)]));

        Assert.Contains("sync", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Definitions_are_ordered_by_key()
    {
        var registry = new JobRegistry([new JobDefinition("b-job", Daily), new JobDefinition("a-job", Daily)]);

        Assert.Equal(["a-job", "b-job"], registry.Definitions.Select(d => d.Key));
        Assert.NotNull(registry.Find("b-job"));
        Assert.Null(registry.Find("missing"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Sync")]
    [InlineData("sync now")]
    public void Invalid_keys_are_rejected(string key) =>
        Assert.Throws<ArgumentException>(() => new JobDefinition(key, Daily));

    [Fact]
    public void Default_max_duration_is_30_minutes() =>
        Assert.Equal(TimeSpan.FromMinutes(30), new JobDefinition("sync", Daily).MaxDuration);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(15, true)]
    [InlineData(16, false)]
    public void Timer_interval_is_between_1_and_15_minutes(int minutes, bool valid)
    {
        var result = new SchedulingOptionsValidator().Validate(null, new SchedulingOptions { TimerInterval = TimeSpan.FromMinutes(minutes) });

        Assert.Equal(valid, result.Succeeded);
    }
}
