using StoneFlow.Integrations.Application.WooCommerce.Backfill;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Integrations.Application.WooCommerce.Status;

/// <summary>Stan synchronizacji WooCommerce dla SF-14 (stany INT-01 wylicza frontend).</summary>
public sealed record GetSyncStatusQuery : IQuery<SyncStatus>;

/// <summary>Stan synchronizacji.</summary>
/// <param name="Configured">Czy skonfigurowano klucze REST.</param>
/// <param name="LastSuccessAtUtc">Kursor ostatniego sukcesu; <c>null</c> przed pierwszym.</param>
/// <param name="LastRun">Ostatni przebieg; <c>running</c> bez ważnej dzierżawy jako <c>abandoned</c>.</param>
/// <param name="IsRunning">Dzierżawa zadania ważna albo „Synchronizuj teraz” przyjęte i niezakończone.</param>
/// <param name="OpenErrors">Liczba otwartych błędów zamówień.</param>
public sealed record SyncStatus(bool Configured, DateTime? LastSuccessAtUtc, SyncRunView? LastRun, bool IsRunning, int OpenErrors);

public sealed class GetSyncStatusHandler(
    IWooCommerceRestSettings settings,
    IWooCommerceSyncStore store,
    IWooCommerceOrderErrors errors,
    IJobStatusReader jobs,
    IManualSyncQueue queue) : IQueryHandler<GetSyncStatusQuery, SyncStatus>
{
    public async Task<SyncStatus> HandleAsync(GetSyncStatusQuery query, CancellationToken cancellationToken)
    {
        var leaseValid = await BackfillLease.IsValidAsync(jobs, cancellationToken);
        var lastRun = await store.GetLastRunAsync(cancellationToken);
        if (lastRun is { Status: SyncRunStatus.Running } && !leaseValid)
        {
            lastRun = lastRun with { Status = SyncRunStatus.Abandoned };
        }

        return new SyncStatus(
            settings.IsConfigured,
            await store.GetCursorAsync(cancellationToken),
            lastRun,
            leaseValid || queue.IsPending,
            await errors.CountOpenAsync(cancellationToken));
    }
}

/// <summary>Dzierżawa zadania uzupełniania z harmonogramu SF-11.</summary>
internal static class BackfillLease
{
    public static async Task<bool> IsValidAsync(IJobStatusReader jobs, CancellationToken cancellationToken) =>
        (await jobs.ReadAsync(cancellationToken)).Any(j => j.Key == WooCommerceBackfill.JobKey && j.IsRunning);
}
