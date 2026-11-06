using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Shared.Infrastructure.Scheduling;

/// <summary>
/// Sprawdzenie należnych zadań (pętla harmonogramu, testy) i uruchomienie na żądanie
/// (<see cref="IJobRunner"/>). Stan i dzierżawa są w SQL; ta klasa nie trzyma stanu między wywołaniami.
/// </summary>
public sealed partial class JobScheduler : IJobRunner
{
    private readonly JobRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly JobExecutor _executor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JobScheduler> _logger;

    internal JobScheduler(
        JobRegistry registry,
        IServiceScopeFactory scopeFactory,
        JobExecutor executor,
        TimeProvider timeProvider,
        ILogger<JobScheduler> logger)
    {
        _registry = registry;
        _scopeFactory = scopeFactory;
        _executor = executor;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Jedno sprawdzenie: każde należne zadanie uruchamia się raz, po kolei. Bez zarejestrowanych zadań
    /// nie łączy się z bazą. Błąd jednego zadania nie zatrzymuje sprawdzenia pozostałych.
    /// </summary>
    public async Task CheckDueAsync(JobTrigger trigger, CancellationToken cancellationToken)
    {
        if (trigger == JobTrigger.Manual)
        {
            throw new ArgumentOutOfRangeException(nameof(trigger), trigger, "Sprawdzenie należności nie jest uruchomieniem ręcznym.");
        }

        foreach (var definition in _registry.Definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await RunIfDueAsync(definition, trigger, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogCheckFailed(exception, definition.Key);
            }
        }
    }

    public async Task<JobRunOutcome> TryRunNowAsync(string jobKey, CancellationToken cancellationToken)
    {
        if (_registry.Find(jobKey) is not { } definition)
        {
            return new JobRunOutcome.UnknownJob();
        }

        // Druga próba: dzierżawę mogła zwolnić inna instancja między odczytem a przejęciem.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var now = _timeProvider.GetUtcNow();
            var state = await PrepareAsync(definition, now, cancellationToken);
            if (state.LeaseExpiresAtUtc is { } lease && Utc(lease) > now)
            {
                break;
            }

            if (await TryStartAsync(definition, state, JobTrigger.Manual, dueAt: now, now, cancellationToken) is { } completed)
            {
                return new JobRunOutcome.Completed(completed);
            }
        }

        return new JobRunOutcome.AlreadyRunning();
    }

    private async Task RunIfDueAsync(JobDefinition definition, JobTrigger trigger, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var state = await PrepareAsync(definition, now, cancellationToken);
        if (!JobDue.IsDue(definition.Schedule, ToDueState(state), now))
        {
            return;
        }

        await TryStartAsync(definition, state, trigger, definition.Schedule.LatestSlotAtOrBefore(now), now, cancellationToken);
    }

    private async Task<JobState> PrepareAsync(JobDefinition definition, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<JobLeaseStore>();
        await store.EnsureJobAsync(definition.Key, now.UtcDateTime, cancellationToken);
        return await store.GetAsync(definition.Key, cancellationToken)
            ?? throw new InvalidOperationException($"Brak stanu zadania '{definition.Key}' po jego wstawieniu.");
    }

    /// <returns>Status zakończonego przebiegu albo <c>null</c>, gdy dzierżawy nie udało się przejąć.</returns>
    private async Task<JobRunStatus?> TryStartAsync(
        JobDefinition definition,
        JobState state,
        JobTrigger trigger,
        DateTimeOffset dueAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var run = new JobRun
        {
            Id = Guid.NewGuid(),
            JobKey = definition.Key,
            Trigger = JobCodes.Of(trigger),
            DueAtUtc = dueAt.UtcDateTime,
            StartedAtUtc = now.UtcDateTime,
            Status = JobCodes.Of(JobRunStatus.Running),
        };

        bool acquired;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<JobLeaseStore>();
            acquired = await store.TryAcquireAsync(state, run, JobExecutor.LeaseUntil(now, definition), cancellationToken);
        }

        if (!acquired)
        {
            LogSkipped(definition.Key);
            return null;
        }

        var context = new JobRunContext(run.Id, dueAt, Utc(state.LastSuccessStartedAtUtc), trigger);
        return await _executor.ExecuteAsync(definition, context, now, cancellationToken);
    }

    private static JobDueState ToDueState(JobState state) => new(
        Utc(state.RegisteredAtUtc),
        Utc(state.LastSuccessStartedAtUtc),
        Utc(state.NextAttemptAtUtc),
        Utc(state.LeaseExpiresAtUtc));

    internal static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    internal static DateTimeOffset? Utc(DateTime? value) => value is { } v ? Utc(v) : null;

    [LoggerMessage(EventId = 7110, Level = LogLevel.Error, Message = "Zadanie {JobKey}: nie udało się sprawdzić ani uruchomić zadania.")]
    private partial void LogCheckFailed(Exception exception, string jobKey);

    [LoggerMessage(EventId = 7111, Level = LogLevel.Information, Message = "Zadanie {JobKey}: działa w innym przebiegu, pominięte.")]
    private partial void LogSkipped(string jobKey);
}
