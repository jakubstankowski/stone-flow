using Microsoft.EntityFrameworkCore;
using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Shared.Infrastructure.Scheduling;

/// <summary>Stan zarejestrowanych zadań do <c>GET /api/jobs</c> i ostrzeżeń w widokach.</summary>
internal sealed class JobStatusReader(JobRegistry registry, SchedulingDbContext db, TimeProvider timeProvider) : IJobStatusReader
{
    public async Task<IReadOnlyList<JobStatusDto>> ReadAsync(CancellationToken cancellationToken)
    {
        if (registry.Definitions.Count == 0)
        {
            return [];
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var keys = registry.Definitions.Select(d => d.Key).ToList();
        var states = await db.Jobs.AsNoTracking()
            .Where(j => keys.Contains(j.JobKey))
            .ToDictionaryAsync(j => j.JobKey, StringComparer.Ordinal, cancellationToken);

        var result = new List<JobStatusDto>(keys.Count);
        foreach (var definition in registry.Definitions)
        {
            states.TryGetValue(definition.Key, out var state);
            var isRunning = state?.LeaseExpiresAtUtc is { } lease && lease > now;
            var lastRun = state is null
                ? null
                : await db.JobRuns.AsNoTracking()
                    .Where(r => r.JobKey == definition.Key)
                    .OrderByDescending(r => r.StartedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);

            result.Add(new JobStatusDto(
                definition.Key,
                definition.Schedule.Describe(),
                isRunning,
                lastRun is null ? null : ToDto(lastRun, isRunning),
                JobScheduler.Utc(state?.LastSuccessStartedAtUtc),
                state?.ConsecutiveFailures ?? 0,
                JobScheduler.Utc(state?.NextAttemptAtUtc)));
        }

        return result;
    }

    /// <summary>
    /// Przebieg <c>running</c> bez ważnej dzierżawy został porzucony (awaria procesu); w bazie oznaczy go
    /// dopiero następne przejęcie dzierżawy, ale stan pokazuje to od razu.
    /// </summary>
    private static JobRunDto ToDto(JobRun run, bool isRunning)
    {
        var status = JobCodes.ParseStatus(run.Status);
        if (status == JobRunStatus.Running && !isRunning)
        {
            status = JobRunStatus.Abandoned;
        }

        return new JobRunDto(JobCodes.Of(status), JobScheduler.Utc(run.StartedAtUtc), JobScheduler.Utc(run.FinishedAtUtc), run.Error);
    }
}
