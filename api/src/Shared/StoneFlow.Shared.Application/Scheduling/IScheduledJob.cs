namespace StoneFlow.Shared.Application.Scheduling;

/// <summary>
/// Zadanie automatyczne uruchamiane przez harmonogram (SF-11). Rejestracja z definicją
/// (<see cref="JobDefinition"/>) przez <c>AddScheduledJob</c> w infrastrukturze modułu.
/// </summary>
/// <remarks>
/// Zadanie musi być bezpieczne do ponownego uruchomienia po przerwanym przebiegu (niezmiennik 4) i po
/// anulowaniu tokenu przerwać pracę bez dalszych zapisów. Komunikaty jego wyjątków trafiają do historii
/// przebiegów, więc nie mogą zawierać sekretów ani danych osobowych. Zadanie biegnie w nowym zakresie
/// DI; dla wielu jednostek pracy tworzy własne zakresy.
/// </remarks>
public interface IScheduledJob
{
    Task RunAsync(JobRunContext context, CancellationToken cancellationToken);
}
