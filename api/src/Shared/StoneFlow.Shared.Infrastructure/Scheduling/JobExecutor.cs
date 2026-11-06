using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Shared.Infrastructure.Scheduling;

/// <summary>
/// Jeden przebieg zadania z przejętą dzierżawą: osobny zakres DI zadania, przedłużanie dzierżawy,
/// limit czasu, utrata dzierżawy i warunkowy zapis wyniku (SF-11, „Dzierżawa i przebieg”).
/// </summary>
internal sealed partial class JobExecutor(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<JobExecutor> logger)
{
    /// <summary>Zapas dzierżawy ponad maksymalny czas i czas oczekiwania na koniec po anulowaniu.</summary>
    internal static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    internal const string TimeoutError = "Przekroczono maksymalny czas przebiegu.";

    internal const string InterruptedError = "Przerwano (zamykanie aplikacji lub anulowanie wywołania).";

    internal const string HungError = "Zadanie nie zakończyło się po anulowaniu.";

    internal const string RenewFailedError = "Błąd bazy przy przedłużaniu dzierżawy.";

    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(10);

    private const int MaxRenewFailures = 2;

    public static DateTime LeaseUntil(DateTimeOffset now, JobDefinition definition) =>
        (now + definition.MaxDuration + Grace).UtcDateTime;

    public async Task<JobRunStatus> ExecuteAsync(
        JobDefinition definition,
        JobRunContext context,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        LogStarted(definition.Key, context.RunId, JobCodes.Of(context.Trigger));
        var countsFailures = context.Trigger != JobTrigger.Manual;

        using var timeout = new CancellationTokenSource(definition.MaxDuration, timeProvider);
        using var leaseLost = new CancellationTokenSource();
        using var jobCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token, leaseLost.Token);
        using var stopRenewal = new CancellationTokenSource();
        var renewFailed = false;

        var jobTask = RunJobAsync(definition, context, jobCancellation.Token);
        var renewTask = RenewAsync(definition, context.RunId, stopRenewal.Token, onLost: databaseError =>
        {
            renewFailed = databaseError;
            try
            {
                leaseLost.Cancel();
            }
            catch (AggregateException exception)
            {
                // Wyjątek z callbacku tokenu w zadaniu nie może przerwać zapisu wyniku.
                LogCallbackError(exception, definition.Key, context.RunId);
            }
        });
        var graceTask = AfterCancellationAsync(jobCancellation.Token, stopRenewal.Token);

        var finished = await Task.WhenAny(jobTask, graceTask);
        await stopRenewal.CancelAsync();
        await renewTask;

        if (finished != jobTask)
        {
            ObserveLate(jobTask, definition.Key, context.RunId);
            // Zawieszenie przy zamykaniu aplikacji nie jest błędem zadania.
            var counts = countsFailures && !cancellationToken.IsCancellationRequested;
            await WriteAsync((store, ct) => store.MarkHungAsync(
                definition.Key, context.RunId, HungError, Now(), counts, ct));
            LogFailed(definition.Key, context.RunId, HungError);
            return JobRunStatus.Failed;
        }

        var jobError = await ObserveAsync(jobTask);
        var completion = Classify(definition.Key, context.RunId, jobError, countsFailures, timeout, leaseLost, renewFailed, cancellationToken);
        var stillOwned = true;
        await WriteAsync(async (store, ct) => stillOwned = await store.CompleteAsync(
            definition.Key, context.RunId, startedAt.UtcDateTime, Now(), completion, ct));

        if (!stillOwned)
        {
            LogLeaseLost(definition.Key, context.RunId);
            return JobRunStatus.Failed;
        }

        if (completion.Status == JobRunStatus.Succeeded)
        {
            LogSucceeded(definition.Key, context.RunId);
        }
        else
        {
            LogFailed(definition.Key, context.RunId, completion.Error ?? string.Empty);
        }

        return completion.Status;
    }

    private JobCompletion Classify(
        string key,
        Guid runId,
        Exception? jobError,
        bool countsFailures,
        CancellationTokenSource timeout,
        CancellationTokenSource leaseLost,
        bool renewFailed,
        CancellationToken callerToken)
    {
        if (jobError is null)
        {
            return new JobCompletion(JobRunStatus.Succeeded, null, CountsAsFailure: false);
        }

        if (leaseLost.IsCancellationRequested)
        {
            return new JobCompletion(JobRunStatus.Failed, renewFailed ? RenewFailedError : JobLeaseStore.LeaseLostError, countsFailures);
        }

        if (timeout.IsCancellationRequested)
        {
            return new JobCompletion(JobRunStatus.Failed, TimeoutError, countsFailures);
        }

        if (callerToken.IsCancellationRequested)
        {
            return new JobCompletion(JobRunStatus.Failed, InterruptedError, CountsAsFailure: false);
        }

        // Także OperationCanceledException zgłoszony przez samo zadanie (np. limit czasu HttpClient).
        LogJobException(jobError, key, runId);
        return new JobCompletion(JobRunStatus.Failed, JobDue.DescribeError(jobError), countsFailures);
    }

    /// <summary>Wyjątek zakończonego zadania, także dla zadania w stanie <c>Canceled</c>.</summary>
    private static async Task<Exception?> ObserveAsync(Task jobTask)
    {
        try
        {
            await jobTask;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private async Task RunJobAsync(JobDefinition definition, JobRunContext context, CancellationToken cancellationToken)
    {
        await Task.Yield();
        await using var scope = scopeFactory.CreateAsyncScope();
        var job = scope.ServiceProvider.GetRequiredKeyedService<IScheduledJob>(definition.Key);
        await job.RunAsync(context, cancellationToken);
    }

    private async Task RenewAsync(JobDefinition definition, Guid runId, CancellationToken stop, Action<bool> onLost)
    {
        var interval = definition.MaxDuration / 3;
        var failures = 0;
        try
        {
            while (true)
            {
                await Task.Delay(interval, timeProvider, stop);
                bool renewed;
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var store = scope.ServiceProvider.GetRequiredService<JobLeaseStore>();
                    renewed = await store.TryRenewAsync(definition.Key, runId, LeaseUntil(timeProvider.GetUtcNow(), definition), stop);
                    failures = 0;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogRenewError(exception, definition.Key, runId);
                    if (++failures < MaxRenewFailures)
                    {
                        continue;
                    }

                    onLost(true);
                    return;
                }

                if (!renewed)
                {
                    onLost(false);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Przebieg się zakończył.
        }
    }

    /// <summary>Kończy się po upływie zapasu od anulowania zadania; nigdy, jeśli zadanie skończy się wcześniej.</summary>
    private async Task AfterCancellationAsync(CancellationToken jobCancellation, CancellationToken stop)
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (jobCancellation.Register(() => cancelled.TrySetResult()))
        await using (stop.Register(() => cancelled.TrySetCanceled(stop)))
        {
            try
            {
                await cancelled.Task;
                await Task.Delay(Grace, timeProvider, stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // Zatrzymanie następuje dopiero po zakończeniu oczekiwania na zadanie.
            }
        }
    }

    private void ObserveLate(Task jobTask, string key, Guid runId) =>
        _ = jobTask.ContinueWith(
            task => LogLateFinish(task.Exception?.GetBaseException(), key, runId),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    /// <summary>Zapis wyniku niezależny od anulowania przebiegu (także przy zamykaniu), z własnym limitem.</summary>
    private async Task WriteAsync(Func<JobLeaseStore, CancellationToken, Task> write)
    {
        using var timeout = new CancellationTokenSource(CompletionTimeout);
        await using var scope = scopeFactory.CreateAsyncScope();
        await write(scope.ServiceProvider.GetRequiredService<JobLeaseStore>(), timeout.Token);
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    [LoggerMessage(EventId = 7100, Level = LogLevel.Information, Message = "Zadanie {JobKey}: start przebiegu {RunId} ({Trigger}).")]
    private partial void LogStarted(string jobKey, Guid runId, string trigger);

    [LoggerMessage(EventId = 7101, Level = LogLevel.Information, Message = "Zadanie {JobKey}: przebieg {RunId} zakończony sukcesem.")]
    private partial void LogSucceeded(string jobKey, Guid runId);

    [LoggerMessage(EventId = 7102, Level = LogLevel.Warning, Message = "Zadanie {JobKey}: przebieg {RunId} nieudany: {Error}")]
    private partial void LogFailed(string jobKey, Guid runId, string error);

    [LoggerMessage(EventId = 7103, Level = LogLevel.Error, Message = "Zadanie {JobKey}: wyjątek zadania w przebiegu {RunId}.")]
    private partial void LogJobException(Exception exception, string jobKey, Guid runId);

    [LoggerMessage(EventId = 7104, Level = LogLevel.Warning, Message = "Zadanie {JobKey}: przebieg {RunId} utracił dzierżawę; stan zadania bez zmian.")]
    private partial void LogLeaseLost(string jobKey, Guid runId);

    [LoggerMessage(EventId = 7105, Level = LogLevel.Warning, Message = "Zadanie {JobKey}: błąd przedłużania dzierżawy przebiegu {RunId}.")]
    private partial void LogRenewError(Exception exception, string jobKey, Guid runId);

    [LoggerMessage(EventId = 7107, Level = LogLevel.Warning, Message = "Zadanie {JobKey}: wyjątek w obsłudze anulowania przebiegu {RunId}.")]
    private partial void LogCallbackError(Exception exception, string jobKey, Guid runId);

    [LoggerMessage(EventId = 7106, Level = LogLevel.Warning, Message = "Zadanie {JobKey}: przebieg {RunId} zakończył się po uznaniu za zawieszony.")]
    private partial void LogLateFinish(Exception? exception, string jobKey, Guid runId);
}
