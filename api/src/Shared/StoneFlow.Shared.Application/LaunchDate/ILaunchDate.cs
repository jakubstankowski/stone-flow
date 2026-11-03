namespace StoneFlow.Shared.Application.LaunchDate;

/// <summary>
/// Data uruchomienia systemu (PRD sekcja 4, „Przełom uruchomienia”).
/// Jeden dzień z konfiguracji wdrożenia; aplikacja tylko ją odczytuje.
/// </summary>
public interface ILaunchDate
{
    DateOnly Value { get; }
}
