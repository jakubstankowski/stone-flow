using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StoneFlow.Integrations.Application.WooCommerce;
using StoneFlow.Integrations.Application.WooCommerce.Backfill;
using StoneFlow.Integrations.Domain.WooCommerce;

namespace StoneFlow.Integrations.Infrastructure.Persistence;

/// <summary>Kursor i przebiegi uzupełniania; transakcje własnego kontekstu.</summary>
internal sealed class WooCommerceSyncStore(IntegrationsDbContext db) : IWooCommerceSyncStore
{
    public async Task<DateTime?> GetCursorAsync(CancellationToken cancellationToken)
    {
        var cursor = await db.SyncStates.AsNoTracking()
            .Where(s => s.Id == WooCommerceSyncState.SingletonId)
            .Select(s => s.LastSuccessCursorUtc)
            .SingleOrDefaultAsync(cancellationToken);
        return Utc(cursor);
    }

    public async Task StartRunAsync(Guid runId, string source, DateTime startedAtUtc, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SyncRuns
            .Where(r => r.Status == SyncRunStatus.Running)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(r => r.Status, SyncRunStatus.Failed)
                    .SetProperty(r => r.FinishedAtUtc, startedAtUtc)
                    .SetProperty(r => r.Message, WooCommerceBackfill.AbandonedMessage),
                cancellationToken);

        db.SyncRuns.Add(new WooCommerceSyncRun { Id = runId, Source = source, StartedAtUtc = startedAtUtc, Status = SyncRunStatus.Running });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<bool> SucceedRunAsync(
        Guid runId,
        DateTime? cursorUtc,
        DateTime finishedAtUtc,
        SyncRunCounts counts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(counts);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Przebieg oznaczony w międzyczasie jako przerwany: bez commitu, kursor bez zmian.
        if (await FinishAsync(runId, SyncRunStatus.Succeeded, finishedAtUtc, counts, null, cancellationToken) != 1)
        {
            return false;
        }

        if (cursorUtc is { } cursor)
        {
            var updated = await db.SyncStates
                .Where(s => s.Id == WooCommerceSyncState.SingletonId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.LastSuccessCursorUtc, cursor), cancellationToken);
            if (updated == 0)
            {
                // Przebiegi biegną pod dzierżawą zadania, więc wiersza nie wstawia równolegle nikt inny.
                db.SyncStates.Add(new WooCommerceSyncState { Id = WooCommerceSyncState.SingletonId, LastSuccessCursorUtc = cursor });
                await db.SaveChangesAsync(cancellationToken);
                db.ChangeTracker.Clear();
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public Task FailRunAsync(Guid runId, DateTime finishedAtUtc, SyncRunCounts counts, string message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(message);
        return FinishAsync(runId, SyncRunStatus.Failed, finishedAtUtc, counts, OrderErrorRules.Clip(message, OrderErrorRules.MaxMessageLength), cancellationToken);
    }

    public async Task<SyncRunView?> GetLastRunAsync(CancellationToken cancellationToken)
    {
        var run = await db.SyncRuns.AsNoTracking()
            .OrderByDescending(r => r.StartedAtUtc)
            .ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return run is null
            ? null
            : new SyncRunView(
                run.Id,
                run.Source,
                run.Status,
                Utc(run.StartedAtUtc),
                Utc(run.FinishedAtUtc),
                run.OrdersProcessed,
                run.Errors,
                run.Unidentified,
                run.Message);
    }

    private Task<int> FinishAsync(
        Guid runId,
        string status,
        DateTime finishedAtUtc,
        SyncRunCounts counts,
        string? message,
        CancellationToken cancellationToken) =>
        db.SyncRuns
            .Where(r => r.Id == runId && r.Status == SyncRunStatus.Running)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(r => r.Status, status)
                    .SetProperty(r => r.FinishedAtUtc, finishedAtUtc)
                    .SetProperty(r => r.OrdersProcessed, counts.OrdersProcessed)
                    .SetProperty(r => r.Errors, counts.Errors)
                    .SetProperty(r => r.Unidentified, counts.Unidentified)
                    .SetProperty(r => r.Message, message),
                cancellationToken);

    internal static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    internal static DateTime? Utc(DateTime? value) => value is { } v ? Utc(v) : null;
}

/// <summary>
/// Historia błędów zamówień: upsert pod blokadą <c>UPDLOCK, HOLDLOCK</c> po <c>ExternalId</c> i jedno
/// ponowienie przy naruszeniu unikalnego indeksu otwartych błędów; zamknięcie z warunkiem wersji
/// (<see cref="OrderErrorRules.CanClose"/>) pod tą samą blokadą.
/// </summary>
internal sealed class WooCommerceOrderErrors(IntegrationsDbContext db, TimeProvider timeProvider) : IWooCommerceOrderErrors
{
    public async Task RecordAsync(OrderFailure failure, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var externalId = OrderErrorRules.Clip(failure.ExternalId, OrderErrorRules.MaxExternalIdLength);
        var message = OrderErrorRules.Clip(failure.Message, OrderErrorRules.MaxMessageLength);
        var context = OrderErrorRules.Clip(failure.Context, OrderErrorRules.MaxContextLength);
        var modified = WooCommerceSyncStore.Utc(failure.ModifiedAtUtc);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                if (await LockOpenAsync(externalId, cancellationToken) is { } open)
                {
                    open.LastOccurredAtUtc = now;
                    open.Attempts++;
                    open.Message = message;
                    open.Context = context;
                    open.FailedModifiedAtUtc = OrderErrorRules.LaterModified(open.FailedModifiedAtUtc, modified);
                }
                else
                {
                    db.OrderErrors.Add(new WooCommerceOrderError
                    {
                        Id = Guid.NewGuid(),
                        ExternalId = externalId,
                        Status = OrderErrorStatus.Open,
                        Source = failure.Source,
                        FirstOccurredAtUtc = now,
                        LastOccurredAtUtc = now,
                        Attempts = 1,
                        Message = message,
                        Context = context,
                        FailedModifiedAtUtc = modified,
                    });
                }

                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException exception) when (attempt == 0 && exception.InnerException is SqlException { Number: 2601 or 2627 })
            {
                // Równoczesne wstawienie otwartego błędu tego zamówienia: ponowienie jako zmiana.
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<bool> CloseAsync(
        string externalId,
        DateTime? modifiedAtUtc,
        bool isDeletion,
        string closedBy,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(closedBy);

        // Zwykły przypadek (brak otwartego błędu) bez transakcji i blokad.
        if (!await db.OrderErrors.AnyAsync(e => e.ExternalId == externalId && e.Status == OrderErrorStatus.Open, cancellationToken))
        {
            return false;
        }

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            if (await LockOpenAsync(externalId, cancellationToken) is not { } open
                || !OrderErrorRules.CanClose(WooCommerceSyncStore.Utc(open.FailedModifiedAtUtc), WooCommerceSyncStore.Utc(modifiedAtUtc), isDeletion))
            {
                return false;
            }

            open.Status = OrderErrorStatus.Closed;
            open.ClosedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            open.ClosedBy = closedBy;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<OrderErrorView?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var error = await db.OrderErrors.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        return error is null ? null : ToView(error);
    }

    public async Task<OrderErrorPage> ListAsync(string? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.OrderErrors.AsNoTracking();
        if (status is not null)
        {
            query = query.Where(e => e.Status == status);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(e => e.LastOccurredAtUtc)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new OrderErrorPage([.. items.Select(ToView)], total);
    }

    public Task<int> CountOpenAsync(CancellationToken cancellationToken) =>
        db.OrderErrors.CountAsync(e => e.Status == OrderErrorStatus.Open, cancellationToken);

    /// <summary>Otwarty błąd zamówienia z blokadą do końca transakcji (także zakresu bez wiersza).</summary>
    private Task<WooCommerceOrderError?> LockOpenAsync(string externalId, CancellationToken cancellationToken) =>
        db.OrderErrors
            .FromSql($"SELECT * FROM [integrations].[WooCommerceOrderErrors] WITH (UPDLOCK, HOLDLOCK) WHERE [ExternalId] = {externalId} AND [Status] = N'open'")
            .SingleOrDefaultAsync(cancellationToken);

    private static OrderErrorView ToView(WooCommerceOrderError error) => new(
        error.Id,
        error.ExternalId,
        error.Status,
        error.Source,
        WooCommerceSyncStore.Utc(error.FirstOccurredAtUtc),
        WooCommerceSyncStore.Utc(error.LastOccurredAtUtc),
        error.Attempts,
        error.Message,
        error.Context,
        WooCommerceSyncStore.Utc(error.ClosedAtUtc),
        error.ClosedBy);
}
