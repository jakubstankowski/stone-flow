namespace StoneFlow.Shared.Application.Scheduling;

/// <summary>Stały klucz, harmonogram i maksymalny czas przebiegu zadania.</summary>
public sealed record JobDefinition
{
    public const int MaxKeyLength = 100;

    public static readonly TimeSpan DefaultMaxDuration = TimeSpan.FromMinutes(30);

    public JobDefinition(string key, JobSchedule schedule, TimeSpan? maxDuration = null)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (!IsValidKey(key))
        {
            throw new ArgumentException(
                $"Klucz zadania '{key}' musi mieć 1–{MaxKeyLength} znaków [a-z0-9-].", nameof(key));
        }

        var duration = maxDuration ?? DefaultMaxDuration;
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDuration), duration, "Maksymalny czas przebiegu musi być dodatni.");
        }

        Key = key;
        Schedule = schedule;
        MaxDuration = duration;
    }

    public string Key { get; }

    public JobSchedule Schedule { get; }

    public TimeSpan MaxDuration { get; }

    public static bool IsValidKey(string? key) =>
        !string.IsNullOrEmpty(key)
        && key.Length <= MaxKeyLength
        && key.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
}
