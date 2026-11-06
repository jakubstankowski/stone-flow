using StoneFlow.Integrations.Application.WooCommerce.Backfill;
using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Integrations.Infrastructure.WooCommerce;

/// <summary>
/// Zadanie harmonogramu <c>woocommerce-backfill</c> (23:00 Europe/Warsaw, SF-13): przebieg
/// uzupełniania z Id przebiegu harmonogramu; uruchomienie ręczne to „Synchronizuj teraz”.
/// </summary>
internal sealed class BackfillJob(WooCommerceBackfill backfill) : IScheduledJob
{
    public static readonly JobDefinition Definition =
        new(WooCommerceBackfill.JobKey, JobSchedule.DailyAt(new TimeOnly(23, 0)), TimeSpan.FromMinutes(30));

    public Task RunAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var source = context.Trigger == JobTrigger.Manual ? SyncRunSource.Manual : SyncRunSource.Schedule;
        return backfill.RunAsync(context.RunId, source, cancellationToken);
    }
}
