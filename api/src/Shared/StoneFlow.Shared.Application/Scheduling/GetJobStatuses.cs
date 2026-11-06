using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Shared.Application.Scheduling;

/// <summary>Stan zarejestrowanych zadań: ostatni przebieg, ostatni sukces, błędy (SF-11).</summary>
public sealed record GetJobStatusesQuery : IQuery<IReadOnlyList<JobStatusDto>>;

public sealed record JobStatusDto(
    string Key,
    string Schedule,
    bool IsRunning,
    JobRunDto? LastRun,
    DateTimeOffset? LastSuccessStartedAtUtc,
    int ConsecutiveFailures,
    DateTimeOffset? NextAttemptAtUtc);

/// <summary>Przebieg zadania; <c>Status</c> to kod: <c>running</c>, <c>succeeded</c>, <c>failed</c>, <c>abandoned</c>.</summary>
public sealed record JobRunDto(string Status, DateTimeOffset StartedAtUtc, DateTimeOffset? FinishedAtUtc, string? Error);

/// <summary>Port odczytu stanu zadań (implementacja w infrastrukturze harmonogramu).</summary>
public interface IJobStatusReader
{
    Task<IReadOnlyList<JobStatusDto>> ReadAsync(CancellationToken cancellationToken);
}

public sealed class GetJobStatusesHandler(IJobStatusReader reader) : IQueryHandler<GetJobStatusesQuery, IReadOnlyList<JobStatusDto>>
{
    public Task<IReadOnlyList<JobStatusDto>> HandleAsync(GetJobStatusesQuery query, CancellationToken cancellationToken) =>
        reader.ReadAsync(cancellationToken);
}
