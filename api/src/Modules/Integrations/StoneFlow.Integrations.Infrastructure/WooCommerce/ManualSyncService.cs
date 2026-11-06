using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StoneFlow.Integrations.Application.WooCommerce;
using StoneFlow.Integrations.Application.WooCommerce.Backfill;
using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Integrations.Infrastructure.WooCommerce;

/// <summary>
/// „Synchronizuj teraz” w tle (SF-13): jedno miejsce oczekiwania. Flaga (0/1) jest ustawiana przy
/// przyjęciu żądania i zwalniana dopiero po zakończeniu <see cref="IJobRunner.TryRunNowAsync"/>, więc
/// drugie żądanie w tym czasie dostaje <c>409</c>. Żądanie oczekujące ginie przy restarcie aplikacji.
/// </summary>
internal sealed partial class ManualSyncService(IJobRunner runner, ILogger<ManualSyncService> logger) : BackgroundService, IManualSyncQueue
{
    private readonly SemaphoreSlim _signal = new(0, 1);
    private int _pending;

    public bool IsPending => Volatile.Read(ref _pending) == 1;

    public bool TryRequest()
    {
        if (Interlocked.CompareExchange(ref _pending, 1, 0) != 0)
        {
            return false;
        }

        _signal.Release();
        return true;
    }

    public override void Dispose()
    {
        _signal.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var outcome = await runner.TryRunNowAsync(WooCommerceBackfill.JobKey, stoppingToken);
                LogFinished(outcome.GetType().Name);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogFailed(exception);
            }
            finally
            {
                Volatile.Write(ref _pending, 0);
            }
        }
    }

    [LoggerMessage(EventId = 7310, Level = LogLevel.Information, Message = "Synchronizuj teraz (WooCommerce): uruchomienie zakończone ({Outcome}).")]
    private partial void LogFinished(string outcome);

    [LoggerMessage(EventId = 7311, Level = LogLevel.Error, Message = "Synchronizuj teraz (WooCommerce): uruchomienie nie powiodło się.")]
    private partial void LogFailed(Exception exception);
}
