using StoneFlow.Integrations.Application.WooCommerce.Status;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Integrations.Application.WooCommerce.Sync;

/// <summary>Wynik „Synchronizuj teraz”.</summary>
public enum RequestSyncOutcome
{
    /// <summary><c>202</c>: przebieg ruszy w tle.</summary>
    Accepted,

    /// <summary><c>409</c> <c>sync_in_progress</c>: przebieg trwa albo inne żądanie czeka.</summary>
    InProgress,

    /// <summary><c>409</c> <c>integration_not_configured</c>.</summary>
    NotConfigured,
}

/// <summary>„Synchronizuj teraz” (SF-13): to samo uzupełnianie od ostatniego sukcesu, w tle.</summary>
public sealed record RequestSyncCommand : ICommand<RequestSyncOutcome>;

/// <summary>
/// Przyjmuje żądanie, gdy dzierżawa zadania nie jest ważna i żadne inne żądanie nie czeka ani nie
/// trwa. Przebieg uruchamia usługa w tle przez <c>IJobRunner</c> (ta sama dzierżawa co harmonogram).
/// </summary>
public sealed class RequestSyncHandler(
    IWooCommerceRestSettings settings,
    IJobStatusReader jobs,
    IManualSyncQueue queue) : ICommandHandler<RequestSyncCommand, RequestSyncOutcome>
{
    public async Task<RequestSyncOutcome> HandleAsync(RequestSyncCommand command, CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
        {
            return RequestSyncOutcome.NotConfigured;
        }

        if (await BackfillLease.IsValidAsync(jobs, cancellationToken))
        {
            return RequestSyncOutcome.InProgress;
        }

        return queue.TryRequest() ? RequestSyncOutcome.Accepted : RequestSyncOutcome.InProgress;
    }
}
