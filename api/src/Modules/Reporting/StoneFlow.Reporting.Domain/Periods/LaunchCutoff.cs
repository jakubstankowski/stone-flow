namespace StoneFlow.Reporting.Domain.Periods;

/// <summary>
/// Przełom uruchomienia (PRD §4): raporty i lista „Operacje” liczą dane wyłącznie od daty
/// uruchomienia. Jedna reguła dla Reporting (SF-17 deleguje do niej przez <c>CashFlowPolicy</c>).
/// </summary>
public static class LaunchCutoff
{
    /// <summary>
    /// Pierwszy liczony dzień: późniejszy z początku okresu i daty uruchomienia. Gdy wynik jest
    /// późniejszy niż koniec okresu, okres w całości leży przed uruchomieniem.
    /// </summary>
    public static DateOnly EffectiveFrom(DateOnly from, DateOnly launchDate) => from > launchDate ? from : launchDate;
}
