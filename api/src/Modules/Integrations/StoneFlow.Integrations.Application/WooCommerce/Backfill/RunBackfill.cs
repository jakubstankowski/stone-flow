using StoneFlow.Integrations.Domain.WooCommerce;
using StoneFlow.Shared.Application.LaunchDate;
using StoneFlow.Shared.Application.Scheduling;
using StoneFlow.Shared.Application.ShopOrders;

namespace StoneFlow.Integrations.Application.WooCommerce.Backfill;

/// <summary>Stabilne kody źródła przebiegu uzupełniania.</summary>
public static class SyncRunSource
{
    public const string Schedule = "schedule";
    public const string Manual = "manual";
}

/// <summary>Stabilne kody statusu przebiegu uzupełniania (baza i API).</summary>
public static class SyncRunStatus
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";

    /// <summary>Tylko w odczycie: <c>running</c> bez ważnej dzierżawy zadania (liczy się jak nieudany).</summary>
    public const string Abandoned = "abandoned";
}

/// <summary>
/// Nieudany przebieg uzupełniania; komunikat złożony przez kod (bez treści odpowiedzi sklepu, sekretów
/// i danych klienta), bo trafia do historii przebiegów harmonogramu.
/// </summary>
public sealed class WooCommerceBackfillException : Exception
{
    public WooCommerceBackfillException()
        : base("Uzupełnianie zamówień WooCommerce nie powiodło się.")
    {
    }

    public WooCommerceBackfillException(string message)
        : base(message)
    {
    }

    public WooCommerceBackfillException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Przebieg uzupełniania zamówień (SF-13, „Kursor i przebieg uzupełniania”), wspólny dla harmonogramu
/// i „Synchronizuj teraz”; biegnie pod dzierżawą zadania <see cref="JobKey"/>. Zestaw <c>any</c>
/// z oknem dat, potem zestaw <c>trash</c>; każda pozycja przez <see cref="OrderOutcome"/>. Sukces
/// przesuwa kursor na górną granicę okna; niepowodzenie zostawia kursor i zgłasza wyjątek.
/// Po anulowaniu nie zapisuje niczego.
/// </summary>
public sealed class WooCommerceBackfill(
    IWooCommerceRestSettings settings,
    IWooCommerceRestClient client,
    IWooCommerceSyncStore store,
    OrderOutcome outcome,
    ILaunchDate launchDate,
    TimeProvider timeProvider,
    IWooCommerceSyncEvents events)
{
    public const string JobKey = "woocommerce-backfill";

    /// <summary>Rozmiar strony REST v3 (maksimum).</summary>
    public const int PageSize = 100;

    /// <summary>Bezpiecznik paginacji: więcej stron w jednym zestawie oznacza błąd odpowiedzi sklepu.</summary>
    public const int MaxPages = 1000;

    public const string AbandonedMessage = "Przerwany (restart lub awaria)";

    public const string NotRunningMessage = "Przebieg oznaczono jako przerwany przed zapisem wyniku; kursor bez zmian";

    /// <summary>Wykonuje przebieg.</summary>
    /// <param name="runId">Id przebiegu (to samo co w historii harmonogramu).</param>
    /// <param name="source">Źródło (<see cref="SyncRunSource"/>).</param>
    /// <param name="cancellationToken">Token anulowania przebiegu.</param>
    /// <exception cref="WooCommerceBackfillException">Przebieg nieudany (zapisany jako <c>failed</c>).</exception>
    public async Task RunAsync(Guid runId, string source, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (!settings.IsConfigured)
        {
            events.NotConfigured();
            return;
        }

        var startedAt = timeProvider.GetUtcNow().UtcDateTime;
        await store.StartRunAsync(runId, source, startedAt, cancellationToken);
        var cursor = await store.GetCursorAsync(cancellationToken);
        var window = BackfillWindow.For(cursor, launchDate.Value, startedAt, WarsawTimeZone.Zone);
        events.RunStarted(runId, source, window.FromUtc, window.ToUtc);

        var progress = new Progress(runId.ToString());
        if (window.IsEmpty)
        {
            await SucceedAsync(runId, cursorUtc: null, progress, cancellationToken);
            return;
        }

        var failure = await FetchSetAsync(new RestPageRequest(BackfillSet.Any, window.FromUtc, window.ToUtc, 1), progress, cancellationToken)
            ?? await FetchSetAsync(new RestPageRequest(BackfillSet.Trash, null, null, 1), progress, cancellationToken);

        // Po anulowaniu przebieg nie zapisuje niczego (kontrakt IScheduledJob).
        cancellationToken.ThrowIfCancellationRequested();
        if (failure is not null)
        {
            await store.FailRunAsync(runId, Now(), progress.Counts, failure, cancellationToken);
            events.RunFailed(runId, failure);
            throw new WooCommerceBackfillException(failure);
        }

        await SucceedAsync(runId, window.ToUtc, progress, cancellationToken);
    }

    /// <summary>
    /// Sukces: kursor i <c>succeeded</c> w jednej transakcji, tylko gdy przebieg jest nadal <c>running</c>.
    /// Przebieg oznaczony w międzyczasie jako przerwany nie przesuwa kursora i kończy się błędem.
    /// </summary>
    private async Task SucceedAsync(Guid runId, DateTime? cursorUtc, Progress progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await store.SucceedRunAsync(runId, cursorUtc, Now(), progress.Counts, cancellationToken))
        {
            events.RunFailed(runId, NotRunningMessage);
            throw new WooCommerceBackfillException(NotRunningMessage);
        }

        events.RunSucceeded(runId, progress.Counts);
    }

    /// <summary>
    /// Zestaw strona po stronie. Zmiana <c>X-WP-Total</c> względem pierwszej strony oznacza przesunięcie
    /// zbioru: zestaw jest pobierany jeszcze raz od strony 1; druga zmiana z rzędu kończy przebieg błędem.
    /// </summary>
    /// <returns>Kontrolowany komunikat błędu albo <c>null</c> po pobraniu wszystkich stron.</returns>
    private async Task<string?> FetchSetAsync(RestPageRequest first, Progress progress, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var (failure, shifted) = await FetchPagesAsync(first, progress, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            if (!shifted)
            {
                return null;
            }
        }

        return $"Lista zamówień zmieniała się w trakcie pobierania{SetSuffix(first.Set)}";
    }

    private async Task<(string? Failure, bool Shifted)> FetchPagesAsync(RestPageRequest first, Progress progress, CancellationToken cancellationToken)
    {
        int? total = null;
        for (var page = 1; ; page++)
        {
            if (page > MaxPages)
            {
                return ($"Przekroczono limit {MaxPages} stron{SetSuffix(first.Set)}", false);
            }

            var result = await client.GetOrdersPageAsync(first with { Page = page }, cancellationToken);
            if (result is RestPageResult.Failed failed)
            {
                return (failed.Message, false);
            }

            var loaded = (RestPageResult.Loaded)result;
            if (page == 1)
            {
                total = loaded.Total;
            }
            else if (loaded.Total != total)
            {
                return (null, true);
            }

            foreach (var body in loaded.Orders)
            {
                var applied = await outcome.ApplyBodyAsync(ShopOrderTopic.Updated, body, OrderErrorSource.Backfill, progress.Context, cancellationToken);
                progress.Add(applied);

                // Zamówienie niezapisane i bez wpisu w historii zginęłoby za kursorem: przebieg nieudany.
                if (applied.ErrorNotRecorded)
                {
                    return ($"Nie udało się zapisać historii błędu zamówienia {applied.ExternalId}", false);
                }
            }

            var more = loaded.TotalPages is { } pages ? page < pages : loaded.Orders.Count >= PageSize;
            if (!more)
            {
                return (null, false);
            }
        }
    }

    private static string SetSuffix(BackfillSet set) => set == BackfillSet.Trash ? " (kosz)" : string.Empty;

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>Liczniki przebiegu po różnych ID zamówień (powtórzone pozycje liczą się raz).</summary>
    private sealed class Progress(string context)
    {
        private readonly HashSet<string> _processed = new(StringComparer.Ordinal);
        private readonly HashSet<string> _failed = new(StringComparer.Ordinal);
        private int _unidentified;

        public string Context { get; } = context;

        public SyncRunCounts Counts => new(_processed.Count, _failed.Count, _unidentified);

        public void Add(OrderApplyResult result)
        {
            if (result.ExternalId is not { } id)
            {
                _unidentified++;
                return;
            }

            _processed.Add(id);
            if (result.Status == OrderApplyStatus.Applied)
            {
                _failed.Remove(id);
            }
            else
            {
                _failed.Add(id);
            }
        }
    }
}
