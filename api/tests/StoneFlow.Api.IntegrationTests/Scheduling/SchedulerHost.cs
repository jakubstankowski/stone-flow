using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Shared.Application.Scheduling;
using StoneFlow.Shared.Infrastructure.Scheduling;

namespace StoneFlow.Api.IntegrationTests.Scheduling;

/// <summary>Sterowanie zadaniem testowym, wspólne dla hostów jednego testu.</summary>
public sealed class TestJobControl
{
    private int _runs;

    public int Runs => Volatile.Read(ref _runs);

    public List<JobRunContext> Contexts { get; } = [];

    /// <summary>Zachowanie przebiegu; domyślnie natychmiastowy sukces.</summary>
    public Func<JobRunContext, CancellationToken, Task> Behavior { get; set; } = (_, _) => Task.CompletedTask;

    internal Task RunAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _runs);
        lock (Contexts)
        {
            Contexts.Add(context);
        }

        return Behavior(context, cancellationToken);
    }
}

public sealed class TestJob(TestJobControl control) : IScheduledJob
{
    public const string Key = "test-job";

    public Task RunAsync(JobRunContext context, CancellationToken cancellationToken) => control.RunAsync(context, cancellationToken);
}

/// <summary>Host API z zarejestrowanym zadaniem testowym, wspólną bazą i zegarem testu.</summary>
internal sealed class SchedulerHost : IAsyncDisposable
{
    /// <summary>10.01.2026 10:00 czasu polskiego; termin 23:00 = 22:00 UTC.</summary>
    public static readonly DateTimeOffset Start = new(2026, 1, 10, 9, 0, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset Slot = new(2026, 1, 10, 22, 0, 0, TimeSpan.Zero);

    private SchedulerHost(StoneFlowApiFactory factory) => Factory = factory;

    public StoneFlowApiFactory Factory { get; }

    public JobScheduler Scheduler => Factory.Services.GetRequiredService<JobScheduler>();

    public static async Task<SchedulerHost> CreateAsync(
        string connectionString,
        FakeTimeProvider time,
        TestJobControl control,
        TimeSpan? maxDuration = null,
        bool enabled = false,
        bool migrate = true)
    {
        var factory = new StoneFlowApiFactory
        {
            Time = time,
            ConfigureTestServices = services =>
            {
                // Tylko zadanie testowe: zadania modułów (np. uzupełnianie WooCommerce) nie mieszają się z historią.
                services.RemoveAll<JobDefinition>();
                services.AddSingleton(control);
                services.AddScheduledJob<TestJob>(new JobDefinition(TestJob.Key, JobSchedule.DailyAt(new TimeOnly(23, 0)), maxDuration));
            },
        }
            .WithDatabase(connectionString)
            .With("Scheduling:Enabled", enabled ? "true" : "false");

        if (migrate)
        {
            await TestDatabase.MigrateAsync(factory);
        }

        return new SchedulerHost(factory);
    }

    public Task CheckAsync() => Scheduler.CheckDueAsync(JobTrigger.Timer, TestContext.Current.CancellationToken);

    public async Task<JobState> JobAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
        return await db.Jobs.AsNoTracking().SingleAsync(j => j.JobKey == TestJob.Key, TestContext.Current.CancellationToken);
    }

    public async Task<List<JobRun>> RunsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
        return await db.JobRuns.AsNoTracking().OrderBy(r => r.StartedAtUtc).ThenBy(r => r.FinishedAtUtc)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    public async Task ExecuteSqlAsync(FormattableString sql)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(sql, TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => Factory.DisposeAsync();
}

internal static class Waiting
{
    /// <summary>Przesuwa zegar krokami, aż warunek się spełni (timery rejestrowane asynchronicznie).</summary>
    public static async Task AdvanceUntilAsync(FakeTimeProvider time, TimeSpan step, Func<bool> condition, int maxSteps = 20)
    {
        for (var i = 0; i < maxSteps && !condition(); i++)
        {
            time.Advance(step);
            await UntilAsync(condition, TimeSpan.FromMilliseconds(500), throwOnTimeout: false);
        }

        Assert.True(condition(), "Warunek nie został spełniony po przesunięciu zegara.");
    }

    /// <summary>Powtarza odczyt, aż wynik spełni warunek.</summary>
    public static async Task<T> ForAsync<T>(Func<Task<T>> read, Func<T, bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        var value = await read();
        while (!condition(value) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            value = await read();
        }

        Assert.True(condition(value), "Odczyt nie spełnił warunku w czasie.");
        return value;
    }

    public static async Task UntilAsync(Func<bool> condition, TimeSpan? timeout = null, bool throwOnTimeout = true)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        if (throwOnTimeout)
        {
            Assert.True(condition(), "Warunek nie został spełniony w czasie.");
        }
    }
}
