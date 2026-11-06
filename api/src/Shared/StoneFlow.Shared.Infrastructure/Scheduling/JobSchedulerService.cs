using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Shared.Infrastructure.Scheduling;

/// <summary>
/// Pętla harmonogramu: sprawdzenie zaraz po starcie i co <see cref="SchedulingOptions.TimerInterval"/>.
/// Timer tylko wyzwala sprawdzenie; należność, ostatni sukces i dzierżawa są w SQL. Uśpiony proces budzi
/// żądanie HTTP (harmonogram hostingu lub monitoring wywołuje <c>/health</c>).
/// </summary>
internal sealed partial class JobSchedulerService(
    JobScheduler scheduler,
    IOptions<SchedulingOptions> options,
    TimeProvider timeProvider,
    ILogger<JobSchedulerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        await Task.Yield();
        await CheckAsync(JobTrigger.Startup, stoppingToken);

        using var timer = new PeriodicTimer(options.Value.TimerInterval, timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CheckAsync(JobTrigger.Timer, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Zamykanie aplikacji.
        }
    }

    private async Task CheckAsync(JobTrigger trigger, CancellationToken stoppingToken)
    {
        try
        {
            await scheduler.CheckDueAsync(trigger, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Zamykanie aplikacji.
        }
        catch (Exception exception)
        {
            LogLoopError(exception);
        }
    }

    [LoggerMessage(EventId = 7120, Level = LogLevel.Error, Message = "Harmonogram zadań: sprawdzenie nie powiodło się.")]
    private partial void LogLoopError(Exception exception);
}
