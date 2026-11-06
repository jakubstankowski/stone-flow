using Microsoft.Extensions.Options;

namespace StoneFlow.Shared.Infrastructure.Scheduling;

/// <summary>Sekcja konfiguracji <c>Scheduling</c>.</summary>
public sealed class SchedulingOptions
{
    public const string SectionName = "Scheduling";

    public static readonly TimeSpan MinTimerInterval = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan MaxTimerInterval = TimeSpan.FromMinutes(15);

    /// <summary><c>false</c> wyłącza pętlę sprawdzeń (testy); uruchomienie na żądanie działa nadal.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Takt sprawdzeń w procesie.</summary>
    public TimeSpan TimerInterval { get; set; } = TimeSpan.FromMinutes(5);
}

public sealed class SchedulingOptionsValidator : IValidateOptions<SchedulingOptions>
{
    public ValidateOptionsResult Validate(string? name, SchedulingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.TimerInterval >= SchedulingOptions.MinTimerInterval && options.TimerInterval <= SchedulingOptions.MaxTimerInterval
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"'{SchedulingOptions.SectionName}:TimerInterval' musi mieścić się w zakresie {SchedulingOptions.MinTimerInterval}–{SchedulingOptions.MaxTimerInterval}.");
    }
}
