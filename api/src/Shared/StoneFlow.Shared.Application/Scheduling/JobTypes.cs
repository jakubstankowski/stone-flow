namespace StoneFlow.Shared.Application.Scheduling;

/// <summary>Źródło uruchomienia przebiegu.</summary>
public enum JobTrigger
{
    /// <summary>Sprawdzenie zaraz po starcie aplikacji.</summary>
    Startup,

    /// <summary>Okresowe sprawdzenie w procesie.</summary>
    Timer,

    /// <summary>Uruchomienie na żądanie (<see cref="IJobRunner"/>).</summary>
    Manual,
}

/// <summary>Status przebiegu w historii.</summary>
public enum JobRunStatus
{
    Running,
    Succeeded,
    Failed,
    Abandoned,
}

/// <summary>Stabilne kody zapisywane w bazie i zwracane przez API.</summary>
public static class JobCodes
{
    public static string Of(JobTrigger trigger) => trigger switch
    {
        JobTrigger.Startup => "startup",
        JobTrigger.Timer => "timer",
        JobTrigger.Manual => "manual",
        _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, null),
    };

    public static string Of(JobRunStatus status) => status switch
    {
        JobRunStatus.Running => "running",
        JobRunStatus.Succeeded => "succeeded",
        JobRunStatus.Failed => "failed",
        JobRunStatus.Abandoned => "abandoned",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static JobRunStatus ParseStatus(string code) => code switch
    {
        "running" => JobRunStatus.Running,
        "succeeded" => JobRunStatus.Succeeded,
        "failed" => JobRunStatus.Failed,
        "abandoned" => JobRunStatus.Abandoned,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };
}

/// <summary>Dane przebiegu przekazywane zadaniu.</summary>
/// <param name="RunId">Identyfikator przebiegu (logi, historia).</param>
/// <param name="DueAt">Ostatni termin harmonogramu; dla uruchomienia ręcznego chwila startu.</param>
/// <param name="LastSuccessStartedAt">Start ostatniego udanego przebiegu harmonogramu. Nie jest
/// biznesowym kursorem zadania (np. „od ostatniego sukcesu” synchronizacji).</param>
/// <param name="Trigger">Źródło uruchomienia.</param>
public sealed record JobRunContext(Guid RunId, DateTimeOffset DueAt, DateTimeOffset? LastSuccessStartedAt, JobTrigger Trigger);

/// <summary>Wynik próby uruchomienia na żądanie.</summary>
public abstract record JobRunOutcome
{
    private JobRunOutcome()
    {
    }

    /// <summary>Przebieg się odbył; status z historii (<c>Succeeded</c> albo <c>Failed</c>).</summary>
    public sealed record Completed(JobRunStatus Status) : JobRunOutcome;

    /// <summary>Zadanie ma ważną dzierżawę — działa w tym lub innym procesie.</summary>
    public sealed record AlreadyRunning : JobRunOutcome;

    /// <summary>Brak zarejestrowanego zadania o tym kluczu.</summary>
    public sealed record UnknownJob : JobRunOutcome;
}

/// <summary>Uruchomienie zarejestrowanego zadania na żądanie, z tą samą dzierżawą co harmonogram.</summary>
public interface IJobRunner
{
    /// <summary>
    /// Uruchamia zadanie od razu (bez sprawdzania należności) i czeka na koniec przebiegu. Nieudany
    /// przebieg ręczny nie ustawia odstępu ponowień harmonogramu.
    /// </summary>
    Task<JobRunOutcome> TryRunNowAsync(string jobKey, CancellationToken cancellationToken);
}
