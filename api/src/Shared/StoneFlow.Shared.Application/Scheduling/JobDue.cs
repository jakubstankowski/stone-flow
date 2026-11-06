namespace StoneFlow.Shared.Application.Scheduling;

/// <summary>Zapisany stan zadania potrzebny do decyzji o uruchomieniu.</summary>
public sealed record JobDueState(
    DateTimeOffset RegisteredAt,
    DateTimeOffset? LastSuccessStartedAt,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? LeaseExpiresAt);

/// <summary>Reguły należności, odstępu ponowień i opisu błędu (SF-11, „Należność zadania”).</summary>
public static class JobDue
{
    public const int MaxErrorLength = 1000;

    public static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan NextRetryDelay = TimeSpan.FromHours(1);

    /// <summary>
    /// Zadanie jest należne, gdy minął jego pierwszy termin, od ostatniego terminu nie było sukcesu,
    /// odstęp po błędzie minął i nie ma ważnej dzierżawy. Pominięte terminy dają jedno uruchomienie.
    /// </summary>
    public static bool IsDue(JobSchedule schedule, JobDueState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(state);

        var latest = schedule.LatestSlotAtOrBefore(now);
        if (latest < schedule.FirstSlotAtOrAfter(state.RegisteredAt))
        {
            return false;
        }

        if (state.LastSuccessStartedAt is { } success && success >= latest)
        {
            return false;
        }

        if (state.NextAttemptAt is { } next && next > now)
        {
            return false;
        }

        return state.LeaseExpiresAt is not { } lease || lease <= now;
    }

    /// <summary>Najbliższa dozwolona próba po kolejnym błędzie: 15 min po pierwszym, 1 h po następnych.</summary>
    public static DateTimeOffset NextAttemptAfter(int consecutiveFailures, DateTimeOffset failedAt) =>
        failedAt + (consecutiveFailures <= 1 ? FirstRetryDelay : NextRetryDelay);

    /// <summary>Typ i komunikat wyjątku, bez stosu wywołań, obcięte do <see cref="MaxErrorLength"/> znaków.</summary>
    public static string DescribeError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Truncate($"{exception.GetType().Name}: {exception.Message}");
    }

    public static string Truncate(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];
    }
}
