using StoneFlow.Reporting.Domain.Periods;
using StoneFlow.Shared.Kernel.Operations;

namespace StoneFlow.Reporting.Domain.CashFlow;

/// <summary>Kierunek operacji pieniężnej.</summary>
public enum CashDirection
{
    /// <summary>„Wpływ”.</summary>
    Inflow,

    /// <summary>„Wypływ”.</summary>
    Outflow,
}

/// <summary>
/// Reguły przepływów pieniężnych wspólne dla listy „Operacje” (SF-17) i widoku przepływów (SF-20):
/// kierunek rodzaju operacji i efektywny początek okresu (przełom uruchomienia, PRD §4).
/// </summary>
public static class CashFlowPolicy
{
    /// <summary>
    /// Rodzaje wypływów w kolejności podziału „Wypływy według rodzaju” (PRD §5, SF-20): opłacone
    /// wydatki, wypłaty właścicieli, zwroty. To kolejność prezentacji podziału — inna niż kolejność
    /// remisu listy „Operacje” (kolejność deklaracji <see cref="CashOperationKind"/>); każda reguła
    /// ma jedno miejsce. Zawiera każdy rodzaj o kierunku <see cref="CashDirection.Outflow"/>.
    /// </summary>
    public static IReadOnlyList<CashOperationKind> OutflowKinds { get; } =
        [CashOperationKind.PaidExpense, CashOperationKind.OwnerWithdrawal, CashOperationKind.Refund];

    /// <summary>Wpłata to wpływ; zwrot, opłacony wydatek i wypłata właściciela to wypływy.</summary>
    public static CashDirection DirectionOf(CashOperationKind kind) => kind switch
    {
        CashOperationKind.Payment => CashDirection.Inflow,
        CashOperationKind.Refund or CashOperationKind.PaidExpense or CashOperationKind.OwnerWithdrawal => CashDirection.Outflow,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Nieznany rodzaj operacji."),
    };

    /// <summary>
    /// Pierwszy dzień liczonych operacji: późniejszy z początku filtra i daty uruchomienia. Gdy
    /// wynik jest późniejszy niż koniec okresu, okres w całości leży przed uruchomieniem. Deleguje do
    /// wspólnej reguły <see cref="LaunchCutoff"/> (SF-19).
    /// </summary>
    public static DateOnly EffectiveFrom(DateOnly from, DateOnly launchDate) => LaunchCutoff.EffectiveFrom(from, launchDate);
}
