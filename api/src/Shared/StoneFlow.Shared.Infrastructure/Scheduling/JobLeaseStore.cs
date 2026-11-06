using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Shared.Infrastructure.Scheduling;

/// <summary>Sposób zakończenia przebiegu zapisywany przez <see cref="JobLeaseStore"/>.</summary>
/// <param name="Status"><c>Succeeded</c> albo <c>Failed</c>.</param>
/// <param name="Error">Opis błędu dla <c>Failed</c>.</param>
/// <param name="CountsAsFailure">Czy zwiększa licznik kolejnych błędów i ustawia odstęp ponowienia.</param>
internal sealed record JobCompletion(JobRunStatus Status, string? Error, bool CountsAsFailure);

/// <summary>
/// Dzierżawa zadania w SQL z fencingiem: każda zmiana stanu po przejęciu jest warunkowa względem
/// <c>LeaseRunId</c> = Id przebiegu, więc spóźniony przebieg nie nadpisze cudzego stanu.
/// </summary>
internal sealed class JobLeaseStore(SchedulingDbContext db)
{
    internal const string AbandonedError = "Przebieg przerwany (restart lub awaria procesu).";

    internal const string LeaseLostError = "Utracono dzierżawę.";

    private static readonly string RunningCode = JobCodes.Of(JobRunStatus.Running);

    /// <summary>Wstawia wiersz stanu, jeśli go brak; równoczesne wstawienie nie jest błędem.</summary>
    public async Task EnsureJobAsync(string key, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (await db.Jobs.AnyAsync(j => j.JobKey == key, cancellationToken))
        {
            return;
        }

        db.Jobs.Add(new JobState { JobKey = key, RegisteredAtUtc = nowUtc });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2627 or 2601 })
        {
            // Wiersz wstawił w międzyczasie inny proces.
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public Task<JobState?> GetAsync(string key, CancellationToken cancellationToken) =>
        db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.JobKey == key, cancellationToken);

    /// <summary>
    /// Przejmuje dzierżawę, jeśli stan zadania nie zmienił się od odczytu (<paramref name="observed"/>:
    /// ten sam właściciel dzierżawy i ta sama ostatnia próba) i dzierżawa nie jest ważna. Warunek na
    /// ostatniej próbie wyklucza drugi przebieg w okresie, gdy inny proces zdążył w międzyczasie przejąć,
    /// wykonać i zwolnić dzierżawę. W tej samej transakcji oznacza porzucony przebieg poprzedniego
    /// właściciela i wstawia nowy przebieg <c>running</c>.
    /// </summary>
    public async Task<bool> TryAcquireAsync(
        JobState observed,
        JobRun run,
        DateTime leaseUntilUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(run);
        var now = run.StartedAtUtc;
        var key = observed.JobKey;
        var previousRunId = observed.LeaseRunId;
        var lastAttempt = observed.LastAttemptStartedAtUtc;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var acquired = await db.Jobs
            .Where(j => j.JobKey == key
                && j.LeaseRunId == previousRunId
                && j.LastAttemptStartedAtUtc == lastAttempt
                && (j.LeaseExpiresAtUtc == null || j.LeaseExpiresAtUtc <= now))
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(j => j.LeaseRunId, run.Id)
                    .SetProperty(j => j.LeaseExpiresAtUtc, leaseUntilUtc)
                    .SetProperty(j => j.LastAttemptStartedAtUtc, now),
                cancellationToken);
        if (acquired == 0)
        {
            return false;
        }

        if (previousRunId is { } previous)
        {
            await db.JobRuns
                .Where(r => r.Id == previous && r.Status == RunningCode)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(r => r.Status, JobCodes.Of(JobRunStatus.Abandoned))
                        .SetProperty(r => r.FinishedAtUtc, now)
                        .SetProperty(r => r.Error, AbandonedError),
                    cancellationToken);
        }

        db.JobRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return true;
    }

    public async Task<bool> TryRenewAsync(string key, Guid runId, DateTime leaseUntilUtc, CancellationToken cancellationToken) =>
        await db.Jobs
            .Where(j => j.JobKey == key && j.LeaseRunId == runId)
            .ExecuteUpdateAsync(set => set.SetProperty(j => j.LeaseExpiresAtUtc, leaseUntilUtc), cancellationToken) == 1;

    /// <summary>
    /// Zapisuje zakończenie i zwalnia dzierżawę, jeśli przebieg nadal ją posiada. W przeciwnym razie
    /// nie zmienia stanu zadania, a przebieg (jeśli nadal <c>running</c>) oznacza jako utratę dzierżawy.
    /// </summary>
    /// <returns><c>false</c>, gdy dzierżawę przejęto.</returns>
    public async Task<bool> CompleteAsync(
        string key,
        Guid runId,
        DateTime startedAtUtc,
        DateTime finishedAtUtc,
        JobCompletion completion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(completion);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var owned = db.Jobs.Where(j => j.JobKey == key && j.LeaseRunId == runId);
        int updated;
        if (completion.Status == JobRunStatus.Succeeded)
        {
            updated = await owned.ExecuteUpdateAsync(
                set => set
                    .SetProperty(j => j.LastSuccessStartedAtUtc, startedAtUtc)
                    .SetProperty(j => j.ConsecutiveFailures, 0)
                    .SetProperty(j => j.NextAttemptAtUtc, (DateTime?)null)
                    .SetProperty(j => j.LeaseRunId, (Guid?)null)
                    .SetProperty(j => j.LeaseExpiresAtUtc, (DateTime?)null),
                cancellationToken);
        }
        else if (completion.CountsAsFailure)
        {
            var (first, next) = RetryTimes(finishedAtUtc);
            updated = await owned.ExecuteUpdateAsync(
                set => set
                    .SetProperty(j => j.NextAttemptAtUtc, j => j.ConsecutiveFailures == 0 ? first : next)
                    .SetProperty(j => j.ConsecutiveFailures, j => j.ConsecutiveFailures + 1)
                    .SetProperty(j => j.LeaseRunId, (Guid?)null)
                    .SetProperty(j => j.LeaseExpiresAtUtc, (DateTime?)null),
                cancellationToken);
        }
        else
        {
            updated = await owned.ExecuteUpdateAsync(
                set => set
                    .SetProperty(j => j.LeaseRunId, (Guid?)null)
                    .SetProperty(j => j.LeaseExpiresAtUtc, (DateTime?)null),
                cancellationToken);
        }

        var stillOwned = updated == 1;
        var status = stillOwned ? completion.Status : JobRunStatus.Failed;
        var error = stillOwned ? completion.Error : LeaseLostError;
        await FinishRunAsync(runId, status, error, finishedAtUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return stillOwned;
    }

    /// <summary>
    /// Zadanie nie zakończyło się po anulowaniu: przebieg dostaje <c>failed</c>, licznik i odstęp rosną,
    /// ale dzierżawa zostaje do wygaśnięcia, bo zadanie może jeszcze działać.
    /// </summary>
    public async Task MarkHungAsync(
        string key,
        Guid runId,
        string error,
        DateTime finishedAtUtc,
        bool countsAsFailure,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (countsAsFailure)
        {
            var (first, next) = RetryTimes(finishedAtUtc);
            await db.Jobs
                .Where(j => j.JobKey == key && j.LeaseRunId == runId)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(j => j.NextAttemptAtUtc, j => j.ConsecutiveFailures == 0 ? first : next)
                        .SetProperty(j => j.ConsecutiveFailures, j => j.ConsecutiveFailures + 1),
                    cancellationToken);
        }

        await FinishRunAsync(runId, JobRunStatus.Failed, error, finishedAtUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private Task<int> FinishRunAsync(Guid runId, JobRunStatus status, string? error, DateTime finishedAtUtc, CancellationToken cancellationToken) =>
        db.JobRuns
            .Where(r => r.Id == runId && r.Status == RunningCode)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(r => r.Status, JobCodes.Of(status))
                    .SetProperty(r => r.FinishedAtUtc, finishedAtUtc)
                    .SetProperty(r => r.Error, error),
                cancellationToken);

    private static (DateTime? First, DateTime? Next) RetryTimes(DateTime failedAtUtc)
    {
        var failedAt = new DateTimeOffset(DateTime.SpecifyKind(failedAtUtc, DateTimeKind.Utc));
        return (JobDue.NextAttemptAfter(1, failedAt).UtcDateTime, JobDue.NextAttemptAfter(2, failedAt).UtcDateTime);
    }
}
