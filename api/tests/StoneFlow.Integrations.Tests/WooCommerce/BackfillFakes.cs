using System.Text;
using StoneFlow.Integrations.Application.WooCommerce;
using StoneFlow.Integrations.Application.WooCommerce.Backfill;
using StoneFlow.Integrations.Domain.WooCommerce;
using StoneFlow.Shared.Application.LaunchDate;
using StoneFlow.Shared.Application.ShopOrders;

namespace StoneFlow.Integrations.Tests.WooCommerce;

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class FixedLaunchDate(DateOnly value) : ILaunchDate
{
    public DateOnly Value { get; } = value;
}

internal sealed class RestSettings(bool configured) : IWooCommerceRestSettings
{
    public bool IsConfigured { get; } = configured;
}

/// <summary>Sklep w pamięci: odpowiedzi stron kolejno z kolejki zestawu; brak w kolejce → pusta strona.</summary>
internal sealed class FakeRestClient : IWooCommerceRestClient
{
    private readonly Dictionary<BackfillSet, Queue<Func<RestPageRequest, CancellationToken, RestPageResult>>> _pages = new()
    {
        [BackfillSet.Any] = new(),
        [BackfillSet.Trash] = new(),
    };

    public List<RestPageRequest> Requests { get; } = [];

    public Func<string, RestOrderResult> Order { get; set; } = _ => new RestOrderResult.NotFound();

    public static string Json(long id, string status = "processing", string modified = "2026-11-05T10:00:00") => $$"""
        { "id": {{id}}, "status": "{{status}}", "currency": "PLN", "date_created_gmt": "2026-11-04T10:00:00",
          "date_modified_gmt": "{{modified}}", "total": "100.00", "billing": { "first_name": "Jan", "last_name": "Testowy" } }
        """;

    public FakeRestClient Page(BackfillSet set, int? total, int? totalPages, params string[] orders)
    {
        _pages[set].Enqueue((_, _) => new RestPageResult.Loaded([.. orders.Select(Encoding.UTF8.GetBytes)], total, totalPages));
        return this;
    }

    public FakeRestClient Fail(BackfillSet set, string message)
    {
        _pages[set].Enqueue((_, _) => new RestPageResult.Failed(message));
        return this;
    }

    public FakeRestClient Respond(BackfillSet set, Func<RestPageRequest, CancellationToken, RestPageResult> response)
    {
        _pages[set].Enqueue(response);
        return this;
    }

    public Task<RestPageResult> GetOrdersPageAsync(RestPageRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        cancellationToken.ThrowIfCancellationRequested();
        var result = _pages[request.Set].TryDequeue(out var next)
            ? next(request, cancellationToken)
            : new RestPageResult.Loaded([], 0, 0);
        return Task.FromResult(result);
    }

    public Task<RestOrderResult> GetOrderAsync(string externalId, CancellationToken cancellationToken) => Task.FromResult(Order(externalId));
}

internal sealed class FakeSyncRunner : IShopOrderSyncRunner
{
    public List<ShopOrderSnapshot> Applied { get; } = [];

    public Func<ShopOrderSnapshot, ShopOrderSyncResult> Result { get; set; } = _ => ShopOrderSyncResult.Created;

    public Task<ShopOrderSyncResult> ApplyAsync(ShopOrderSnapshot snapshot, CancellationToken cancellationToken)
    {
        Applied.Add(snapshot);
        return Task.FromResult(Result(snapshot));
    }
}

internal sealed class MemorySyncStore : IWooCommerceSyncStore
{
    public DateTime? Cursor { get; set; }

    public Dictionary<Guid, SyncRunView> Runs { get; } = [];

    public Task<DateTime?> GetCursorAsync(CancellationToken cancellationToken) => Task.FromResult(Cursor);

    public Task StartRunAsync(Guid runId, string source, DateTime startedAtUtc, CancellationToken cancellationToken)
    {
        foreach (var run in Runs.Values.Where(r => r.Status == SyncRunStatus.Running).ToList())
        {
            Runs[run.Id] = run with { Status = SyncRunStatus.Failed, Message = WooCommerceBackfill.AbandonedMessage };
        }

        Runs[runId] = new SyncRunView(runId, source, SyncRunStatus.Running, startedAtUtc, null, 0, 0, 0, null);
        return Task.CompletedTask;
    }

    public Task<bool> SucceedRunAsync(Guid runId, DateTime? cursorUtc, DateTime finishedAtUtc, SyncRunCounts counts, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Runs[runId].Status != SyncRunStatus.Running)
        {
            return Task.FromResult(false);
        }

        Cursor = cursorUtc ?? Cursor;
        Finish(runId, SyncRunStatus.Succeeded, finishedAtUtc, counts, null);
        return Task.FromResult(true);
    }

    public Task FailRunAsync(Guid runId, DateTime finishedAtUtc, SyncRunCounts counts, string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Finish(runId, SyncRunStatus.Failed, finishedAtUtc, counts, message);
        return Task.CompletedTask;
    }

    public Task<SyncRunView?> GetLastRunAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Runs.Values.OrderByDescending(r => r.StartedAtUtc).FirstOrDefault());

    private void Finish(Guid runId, string status, DateTime finishedAtUtc, SyncRunCounts counts, string? message) =>
        Runs[runId] = Runs[runId] with
        {
            Status = status,
            FinishedAtUtc = finishedAtUtc,
            OrdersProcessed = counts.OrdersProcessed,
            Errors = counts.Errors,
            Unidentified = counts.Unidentified,
            Message = message,
        };
}

/// <summary>Historia błędów w pamięci z tymi samymi regułami (upsert, warunek wersji) co magazyn SQL.</summary>
internal sealed class MemoryOrderErrors : IWooCommerceOrderErrors
{
    public List<(OrderErrorView View, DateTime? FailedModifiedAtUtc)> Rows { get; } = [];

    /// <summary>Symulacja awarii bazy przy zapisie historii.</summary>
    public bool FailRecording { get; set; }

    public Task RecordAsync(OrderFailure failure, CancellationToken cancellationToken)
    {
        if (FailRecording)
        {
            throw new InvalidOperationException("Symulowany błąd zapisu historii.");
        }

        var index = Rows.FindIndex(r => r.View.ExternalId == failure.ExternalId && r.View.Status == OrderErrorStatus.Open);
        var now = DateTime.UtcNow;
        if (index >= 0)
        {
            var (view, modified) = Rows[index];
            Rows[index] = (view with { Attempts = view.Attempts + 1, Message = failure.Message, Context = failure.Context, LastOccurredAtUtc = now },
                OrderErrorRules.LaterModified(modified, failure.ModifiedAtUtc));
        }
        else
        {
            Rows.Add((new OrderErrorView(Guid.NewGuid(), failure.ExternalId, OrderErrorStatus.Open, failure.Source, now, now, 1, failure.Message, failure.Context, null, null),
                failure.ModifiedAtUtc));
        }

        return Task.CompletedTask;
    }

    public Task<bool> CloseAsync(string externalId, DateTime? modifiedAtUtc, bool isDeletion, string closedBy, CancellationToken cancellationToken)
    {
        var index = Rows.FindIndex(r => r.View.ExternalId == externalId && r.View.Status == OrderErrorStatus.Open);
        if (index < 0 || !OrderErrorRules.CanClose(Rows[index].FailedModifiedAtUtc, modifiedAtUtc, isDeletion))
        {
            return Task.FromResult(false);
        }

        Rows[index] = (Rows[index].View with { Status = OrderErrorStatus.Closed, ClosedBy = closedBy, ClosedAtUtc = DateTime.UtcNow }, Rows[index].FailedModifiedAtUtc);
        return Task.FromResult(true);
    }

    public Task<OrderErrorView?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult<OrderErrorView?>(Rows.Select(r => r.View).SingleOrDefault(v => v.Id == id));

    public Task<OrderErrorPage> ListAsync(string? status, int page, int pageSize, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<int> CountOpenAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Rows.Count(r => r.View.Status == OrderErrorStatus.Open));
}

internal sealed class NullSyncEvents : IWooCommerceSyncEvents
{
    public int NotConfiguredCount { get; private set; }

    public void NotConfigured() => NotConfiguredCount++;

    public void RunStarted(Guid runId, string source, DateTime fromUtc, DateTime toUtc)
    {
    }

    public void RunSucceeded(Guid runId, SyncRunCounts counts)
    {
    }

    public void RunFailed(Guid runId, string message)
    {
    }

    public void Unidentified(string source, string context, string problem)
    {
    }

    public void ApplyFailed(Exception? exception, string source, string externalId)
    {
    }

    public void ErrorRecordFailed(Exception exception, string externalId)
    {
    }
}

/// <summary>Przebieg uzupełniania z fałszywym sklepem i portami w pamięci.</summary>
internal sealed class BackfillHarness
{
    /// <summary>6.11.2026 22:00 UTC (23:00 czasu polskiego).</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 6, 22, 0, 0, TimeSpan.Zero);

    public static readonly DateOnly Launch = new(2026, 11, 3);

    public BackfillHarness(bool configured = true)
    {
        Backfill = new WooCommerceBackfill(
            new RestSettings(configured),
            Client,
            Store,
            new OrderOutcome(Sync, Errors, Events),
            new FixedLaunchDate(Launch),
            new FixedTime(Now),
            Events);
    }

    public FakeRestClient Client { get; } = new();

    public FakeSyncRunner Sync { get; } = new();

    public MemorySyncStore Store { get; } = new();

    public MemoryOrderErrors Errors { get; } = new();

    public NullSyncEvents Events { get; } = new();

    public WooCommerceBackfill Backfill { get; }

    public Guid RunId { get; } = Guid.NewGuid();

    public Task RunAsync(CancellationToken? cancellationToken = null) =>
        Backfill.RunAsync(RunId, SyncRunSource.Schedule, cancellationToken ?? TestContext.Current.CancellationToken);

    public SyncRunView Run => Store.Runs[RunId];
}
