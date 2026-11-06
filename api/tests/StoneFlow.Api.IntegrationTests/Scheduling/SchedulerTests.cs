using Microsoft.Extensions.Time.Testing;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Api.IntegrationTests.Scheduling;

/// <summary>SF-11: trwały harmonogram zadań na SQL Server (restart, uśpienie, równoległość, błędy).</summary>
[Collection(SqlServerCollection.Name)]
public sealed class SchedulerTests(SqlServerFixture sqlServer)
{
    private static readonly TimeSpan AfterSlot = TimeSpan.FromMinutes(10);

    private readonly FakeTimeProvider _time = new(SchedulerHost.Start);
    private readonly TestJobControl _control = new();

    [Fact]
    public async Task Due_job_runs_after_restart()
    {
        var database = sqlServer.NewDatabase("sched");
        await using (var before = await SchedulerHost.CreateAsync(database, _time, _control))
        {
            await before.CheckAsync();
        }

        _time.SetUtcNow(SchedulerHost.Slot + AfterSlot);
        await using var after = await SchedulerHost.CreateAsync(database, _time, _control, migrate: false);
        await after.Scheduler.CheckDueAsync(JobTrigger.Startup, TestContext.Current.CancellationToken);

        Assert.Equal(1, _control.Runs);
        var run = Assert.Single(await after.RunsAsync());
        Assert.Equal("succeeded", run.Status);
        Assert.Equal("startup", run.Trigger);
    }

    [Fact]
    public async Task Startup_check_runs_due_job()
    {
        var database = sqlServer.NewDatabase("sched");
        await using (var before = await SchedulerHost.CreateAsync(database, _time, _control))
        {
            await before.CheckAsync();
        }

        _time.SetUtcNow(SchedulerHost.Slot + AfterSlot);
        await using var after = await SchedulerHost.CreateAsync(database, _time, _control, enabled: true, migrate: false);
        _ = after.Factory.Services;

        await Waiting.UntilAsync(() => _control.Runs == 1);
        var run = await Waiting.ForAsync(after.RunsAsync, runs => runs is [{ Status: "succeeded" }]);
        Assert.Equal("startup", Assert.Single(run).Trigger);
    }

    [Fact]
    public async Task New_job_waits_for_first_slot_and_runs_once_per_period()
    {
        await using var host = await SchedulerHost.CreateAsync(sqlServer.NewDatabase("sched"), _time, _control);

        await host.CheckAsync();
        Assert.Equal(0, _control.Runs);

        _time.SetUtcNow(SchedulerHost.Slot + AfterSlot);
        await host.CheckAsync();
        await host.CheckAsync();
        _time.Advance(TimeSpan.FromHours(1));
        await host.CheckAsync();

        Assert.Equal(1, _control.Runs);
        Assert.Equal(SchedulerHost.Slot, _control.Contexts[0].DueAt);
        Assert.Null(_control.Contexts[0].LastSuccessStartedAt);
    }

    [Fact]
    public async Task Missed_slots_run_once()
    {
        await using var host = await SchedulerHost.CreateAsync(sqlServer.NewDatabase("sched"), _time, _control);
        await host.CheckAsync();
        _time.SetUtcNow(SchedulerHost.Slot + AfterSlot);
        await host.CheckAsync();
        var firstSuccess = (await host.JobAsync()).LastSuccessStartedAtUtc;

        // Trzy dni bez żadnego sprawdzenia (proces uśpiony), potem dwa sprawdzenia.
        _time.Advance(TimeSpan.FromDays(3));
        await host.CheckAsync();
        await host.CheckAsync();

        Assert.Equal(2, _control.Runs);
        Assert.Equal(SchedulerHost.Slot.AddDays(3), _control.Contexts[1].DueAt);
        Assert.Equal(firstSuccess, _control.Contexts[1].LastSuccessStartedAt?.UtcDateTime);
    }

    [Fact]
    public async Task Concurrent_attempts_run_once()
    {
        var database = sqlServer.NewDatabase("sched");
        await using var first = await SchedulerHost.CreateAsync(database, _time, _control);
        await using var second = await SchedulerHost.CreateAsync(database, _time, _control, migrate: false);
        await first.CheckAsync();
        _time.SetUtcNow(SchedulerHost.Slot + AfterSlot);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _control.Behavior = async (_, _) =>
        {
            started.TrySetResult();
            await release.Task;
        };

        var running = first.CheckAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        await second.CheckAsync();
        var manual = await second.Scheduler.TryRunNowAsync(TestJob.Key, TestContext.Current.CancellationToken);
        var manualOnFirst = await first.Scheduler.TryRunNowAsync(TestJob.Key, TestContext.Current.CancellationToken);
        release.SetResult();
        await running;

        Assert.IsType<JobRunOutcome.AlreadyRunning>(manual);
        Assert.IsType<JobRunOutcome.AlreadyRunning>(manualOnFirst);
        Assert.Equal(1, _control.Runs);
        Assert.Equal("succeeded", Assert.Single(await first.RunsAsync()).Status);
    }

    [Fact]
    public async Task Expired_lease_is_abandoned_and_job_reruns_without_backoff()
    {
        await using var host = await SchedulerHost.CreateAsync(sqlServer.NewDatabase("sched"), _time, _control);
        await host.CheckAsync();
        _time.SetUtcNow(SchedulerHost.Slot + AfterSlot);

        // Stan po awarii procesu: przebieg „running” z dzierżawą, która wygasła minutę temu.
        var crashed = Guid.NewGuid();
        var startedAt = (SchedulerHost.Slot - TimeSpan.FromMinutes(40)).UtcDateTime;
        var expired = (_time.GetUtcNow() - TimeSpan.FromMinutes(1)).UtcDateTime;
        await host.ExecuteSqlAsync($"""
            UPDATE scheduling.Jobs SET LeaseRunId = {crashed}, LeaseExpiresAtUtc = {expired} WHERE JobKey = {TestJob.Key};
            INSERT INTO scheduling.JobRuns (Id, JobKey, [Trigger], DueAtUtc, StartedAtUtc, Status)
            VALUES ({crashed}, {TestJob.Key}, 'timer', {startedAt}, {startedAt}, 'running');
            """);

        await host.CheckAsync();

        Assert.Equal(1, _control.Runs);
        var runs = await host.RunsAsync();
        Assert.Equal(2, runs.Count);
        var abandoned = runs.Single(r => r.Id == crashed);
        Assert.Equal("abandoned", abandoned.Status);
        Assert.NotNull(abandoned.Error);
        Assert.Equal("succeeded", runs.Single(r => r.Id != crashed).Status);
        var job = await host.JobAsync();
        Assert.Equal(0, job.ConsecutiveFailures);
        Assert.Null(job.NextAttemptAtUtc);
        Assert.Null(job.LeaseRunId);
    }

    [Fact]
    public async Task Late_completion_does_not_touch_taken_over_lease()
    {
        await using var host = await SchedulerHost.CreateAsync(sqlServer.NewDatabase("sched"), _time, _control);
        await host.CheckAsync();
        _time.SetUtcNow(SchedulerHost.Slot + AfterSlot);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _control.Behavior = async (_, _) =>
        {
            started.TrySetResult();
            await release.Task;
        };

        var running = host.CheckAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        var other = Guid.NewGuid();
        var otherUntil = (_time.GetUtcNow() + TimeSpan.FromHours(1)).UtcDateTime;
        await host.ExecuteSqlAsync($"UPDATE scheduling.Jobs SET LeaseRunId = {other}, LeaseExpiresAtUtc = {otherUntil} WHERE JobKey = {TestJob.Key}");
        release.SetResult();
        await running;

        var job = await host.JobAsync();
        Assert.Equal(other, job.LeaseRunId);
        Assert.Null(job.LastSuccessStartedAtUtc);
        var run = Assert.Single(await host.RunsAsync());
        Assert.Equal("failed", run.Status);
        Assert.Equal("Utracono dzierżawę.", run.Error);
    }

    [Fact]
    public async Task Lost_lease_cancels_running_job()
    {
        var maxDuration = TimeSpan.FromMinutes(3);
        await using var host = await SchedulerHost.CreateAsync(sqlServer.NewDatabase("sched"), _time, _control, maxDuration);
        await host.CheckAsync();
        _time.SetUtcNow(SchedulerHost.Slot + AfterSlot);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        _control.Behavior = async (_, cancellationToken) =>
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                throw;
            }
        };

        var running = host.CheckAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        var other = Guid.NewGuid();
        var otherUntil = (_time.GetUtcNow() + TimeSpan.FromHours(1)).UtcDateTime;
        await host.ExecuteSqlAsync($"UPDATE scheduling.Jobs SET LeaseRunId = {other}, LeaseExpiresAtUtc = {otherUntil} WHERE JobKey = {TestJob.Key}");

        // Przedłużenie co MaxDuration / 3 wykrywa utratę przed limitem czasu.
        await Waiting.AdvanceUntilAsync(_time, TimeSpan.FromSeconds(30), () => Volatile.Read(ref cancelled), maxSteps: 4);
        await running;

        Assert.Equal(other, (await host.JobAsync()).LeaseRunId);
        Assert.Equal("Utracono dzierżawę.", Assert.Single(await host.RunsAsync()).Error);
    }

    [Fact]
    public async Task Failure_sets_backoff_and_success_resets_it()
    {
        await using var host = await SchedulerHost.CreateAsync(sqlServer.NewDatabase("sched"), _time, _control);
        await host.CheckAsync();
        _control.Behavior = (_, _) => throw new InvalidOperationException("Sklep nie odpowiada.");

        _time.SetUtcNow(SchedulerHost.Slot + AfterSlot);
        await host.CheckAsync();
        var afterFirst = await host.JobAsync();
        Assert.Equal(1, afterFirst.ConsecutiveFailures);
        Assert.Equal((SchedulerHost.Slot + AfterSlot + TimeSpan.FromMinutes(15)).UtcDateTime, afterFirst.NextAttemptAtUtc);
        var failed = Assert.Single(await host.RunsAsync());
        Assert.Equal("failed", failed.Status);
        Assert.Equal("InvalidOperationException: Sklep nie odpowiada.", failed.Error);

        _time.Advance(TimeSpan.FromMinutes(10));
        await host.CheckAsync();
        Assert.Equal(1, _control.Runs);

        _time.Advance(TimeSpan.FromMinutes(6));
        await host.CheckAsync();
        var afterSecond = await host.JobAsync();
        Assert.Equal(2, _control.Runs);
        Assert.Equal(2, afterSecond.ConsecutiveFailures);
        Assert.Equal((_time.GetUtcNow() + TimeSpan.FromHours(1)).UtcDateTime, afterSecond.NextAttemptAtUtc);

        _control.Behavior = (_, _) => Task.CompletedTask;
        _time.Advance(TimeSpan.FromHours(1));
        await host.CheckAsync();
        var afterSuccess = await host.JobAsync();
        Assert.Equal(3, _control.Runs);
        Assert.Equal(0, afterSuccess.ConsecutiveFailures);
        Assert.Null(afterSuccess.NextAttemptAtUtc);
        Assert.NotNull(afterSuccess.LastSuccessStartedAtUtc);
    }

    [Fact]
    public async Task Manual_run_ignores_schedule_and_its_failure_sets_no_backoff()
    {
        await using var host = await SchedulerHost.CreateAsync(sqlServer.NewDatabase("sched"), _time, _control);
        _control.Behavior = (_, _) => throw new InvalidOperationException("Błąd ręczny.");

        var failed = await host.Scheduler.TryRunNowAsync(TestJob.Key, TestContext.Current.CancellationToken);
        var job = await host.JobAsync();

        Assert.Equal(new JobRunOutcome.Completed(JobRunStatus.Failed), failed);
        Assert.Equal(0, job.ConsecutiveFailures);
        Assert.Null(job.NextAttemptAtUtc);
        Assert.Equal("manual", Assert.Single(await host.RunsAsync()).Trigger);

        _control.Behavior = (_, _) => Task.CompletedTask;
        Assert.Equal(new JobRunOutcome.Completed(JobRunStatus.Succeeded), await host.Scheduler.TryRunNowAsync(TestJob.Key, TestContext.Current.CancellationToken));
        Assert.IsType<JobRunOutcome.UnknownJob>(await host.Scheduler.TryRunNowAsync("missing", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Timeout_cancels_job_and_counts_as_failure()
    {
        var maxDuration = TimeSpan.FromMinutes(3);
        await using var host = await SchedulerHost.CreateAsync(sqlServer.NewDatabase("sched"), _time, _control, maxDuration);
        await host.CheckAsync();
        _time.SetUtcNow(SchedulerHost.Slot + AfterSlot);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _control.Behavior = async (_, cancellationToken) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        };

        var running = host.CheckAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        await Waiting.AdvanceUntilAsync(_time, TimeSpan.FromMinutes(1), () => running.IsCompleted, maxSteps: 6);
        await running;

        var run = Assert.Single(await host.RunsAsync());
        Assert.Equal("failed", run.Status);
        Assert.Equal("Przekroczono maksymalny czas przebiegu.", run.Error);
        var job = await host.JobAsync();
        Assert.Equal(1, job.ConsecutiveFailures);
        Assert.Null(job.LeaseRunId);
    }

    [Fact]
    public async Task Hung_job_is_failed_with_backoff_and_keeps_lease()
    {
        var maxDuration = TimeSpan.FromMinutes(3);
        await using var host = await SchedulerHost.CreateAsync(sqlServer.NewDatabase("sched"), _time, _control, maxDuration);
        await host.CheckAsync();
        _time.SetUtcNow(SchedulerHost.Slot + AfterSlot);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _control.Behavior = async (_, _) =>
        {
            started.TrySetResult();
            await release.Task; // ignoruje anulowanie
        };

        var running = host.CheckAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        await Waiting.AdvanceUntilAsync(_time, TimeSpan.FromMinutes(1), () => running.IsCompleted, maxSteps: 10);
        await running;

        var run = Assert.Single(await host.RunsAsync());
        Assert.Equal("failed", run.Status);
        Assert.Equal("Zadanie nie zakończyło się po anulowaniu.", run.Error);
        var job = await host.JobAsync();
        Assert.Equal(1, job.ConsecutiveFailures);
        Assert.NotNull(job.NextAttemptAtUtc);
        Assert.Equal(run.Id, job.LeaseRunId);

        // Po wygaśnięciu dzierżawy, ale przed odstępem, zadanie nie rusza ponownie.
        _time.SetUtcNow(Utc(job.LeaseExpiresAtUtc!.Value) + TimeSpan.FromMinutes(1));
        Assert.True(_time.GetUtcNow() < Utc(job.NextAttemptAtUtc!.Value));
        await host.CheckAsync();
        Assert.Equal(1, _control.Runs);

        // Po odstępie rusza; zawieszony przebieg nie jest oznaczany jako porzucony.
        release.SetResult();
        _control.Behavior = (_, _) => Task.CompletedTask;
        _time.SetUtcNow(Utc(job.NextAttemptAtUtc.Value) + TimeSpan.FromMinutes(1));
        await host.CheckAsync();
        Assert.Equal(2, _control.Runs);
        var runs = await host.RunsAsync();
        Assert.Equal("failed", runs.Single(r => r.Id == run.Id).Status);
        Assert.Equal("succeeded", runs.Single(r => r.Id != run.Id).Status);
    }

    [Fact]
    public async Task Shutdown_during_run_is_failed_without_backoff()
    {
        await using var host = await SchedulerHost.CreateAsync(sqlServer.NewDatabase("sched"), _time, _control);
        await host.CheckAsync();
        _time.SetUtcNow(SchedulerHost.Slot + AfterSlot);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _control.Behavior = async (_, cancellationToken) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        };

        using var stopping = new CancellationTokenSource();
        var running = host.Scheduler.CheckDueAsync(JobTrigger.Timer, stopping.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        await stopping.CancelAsync();
        await running;

        var run = Assert.Single(await host.RunsAsync());
        Assert.Equal("failed", run.Status);
        Assert.Equal("Przerwano (zamykanie aplikacji lub anulowanie wywołania).", run.Error);
        var job = await host.JobAsync();
        Assert.Equal(0, job.ConsecutiveFailures);
        Assert.Null(job.NextAttemptAtUtc);
        Assert.Null(job.LeaseRunId);
    }

    [Fact]
    public async Task Cancellation_thrown_by_job_is_recorded_as_its_error()
    {
        await using var host = await SchedulerHost.CreateAsync(sqlServer.NewDatabase("sched"), _time, _control);
        await host.CheckAsync();
        _time.SetUtcNow(SchedulerHost.Slot + AfterSlot);
        _control.Behavior = (_, _) => throw new TaskCanceledException("Limit czasu żądania HTTP.");

        await host.CheckAsync();

        var run = Assert.Single(await host.RunsAsync());
        Assert.Equal("failed", run.Status);
        Assert.Equal("TaskCanceledException: Limit czasu żądania HTTP.", run.Error);
        Assert.Equal(1, (await host.JobAsync()).ConsecutiveFailures);
    }

    [Fact]
    public async Task Simultaneous_checks_on_two_hosts_run_once_per_period()
    {
        var database = sqlServer.NewDatabase("sched");
        await using var first = await SchedulerHost.CreateAsync(database, _time, _control);
        await using var second = await SchedulerHost.CreateAsync(database, _time, _control, migrate: false);
        await first.CheckAsync();
        _time.SetUtcNow(SchedulerHost.Slot + AfterSlot);

        for (var day = 1; day <= 5; day++)
        {
            await Task.WhenAll(first.CheckAsync(), second.CheckAsync(), first.CheckAsync(), second.CheckAsync());
            Assert.Equal(day, _control.Runs);
            _time.Advance(TimeSpan.FromDays(1));
        }
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
