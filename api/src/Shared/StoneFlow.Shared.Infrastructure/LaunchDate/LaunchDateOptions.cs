namespace StoneFlow.Shared.Infrastructure.LaunchDate;

/// <summary>Sekcja konfiguracji <c>StoneFlow</c>.</summary>
public sealed class LaunchDateOptions
{
    public const string SectionName = "StoneFlow";

    public const string LaunchDateKey = SectionName + ":LaunchDate";

    public const string Format = "yyyy-MM-dd";

    /// <summary>Data uruchomienia w formacie <c>yyyy-MM-dd</c>, ustawiana w konfiguracji wdrożenia.</summary>
    public string? LaunchDate { get; set; }
}
