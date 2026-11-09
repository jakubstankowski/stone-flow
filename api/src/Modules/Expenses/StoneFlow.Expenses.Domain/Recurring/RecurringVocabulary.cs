using StoneFlow.Expenses.Domain.Expenses;

namespace StoneFlow.Expenses.Domain.Recurring;

/// <summary>Stan definicji (nieprzechowywany; reguła 7 w <see cref="RecurringSchedule.Status"/>).</summary>
public enum RecurringDefinitionStatus
{
    /// <summary>„Aktywny”.</summary>
    Active,

    /// <summary>„Wstrzymany”: <c>PausedOn</c> uzupełnione.</summary>
    Paused,

    /// <summary>„Zakończony”: ostatni termin w zakresie już minął albo wypada dzisiaj.</summary>
    Ended,
}

/// <summary>Powód przedziału wyłączenia (w bazie nazwa wartości).</summary>
public enum RecurringExclusionReason
{
    /// <summary>Wstrzymanie: <c>[PausedOn, dzień wznowienia)</c>.</summary>
    Pause,

    /// <summary>Przesunięcie początku na wcześniej: <c>[nowy początek, min(stary początek, dzisiaj))</c>.</summary>
    StartMoved,
}

/// <summary>Pola definicji objęte audytem (kolejność jak w zapisie audytu).</summary>
public enum RecurringDefinitionField
{
    Description,
    Category,
    Counterparty,
    GrossAmount,
    NetAmount,
    PaymentMethod,
    Payer,
    Comment,
    DayOfMonth,
    StartsOn,
    EndsOn,
    PausedOn,
}

/// <summary>Treść definicji, kopiowana do tworzonych wystąpień (wartości już znormalizowane).</summary>
public sealed record RecurringContent(
    string Description,
    ExpenseCategory Category,
    string? Counterparty,
    decimal GrossAmount,
    decimal? NetAmount,
    ExpensePaymentMethod? PaymentMethod,
    string? Payer,
    string? Comment);

/// <summary>Harmonogram z formularza: bieżący dzień miesiąca, początek i opcjonalny koniec (włącznie).</summary>
public sealed record RecurringScheduleInput(int DayOfMonth, DateOnly StartsOn, DateOnly? EndsOn);

/// <summary>Naruszenie reguły definicji.</summary>
public enum RecurringRuleViolation
{
    /// <summary>Wstrzymanie definicji już wstrzymanej.</summary>
    AlreadyPaused,

    /// <summary>Wznowienie definicji niewstrzymanej.</summary>
    NotPaused,

    /// <summary>Wstrzymanie definicji zakończonej (bez skutku).</summary>
    Ended,

    /// <summary>Zmiana dnia miesiąca na definicji wstrzymanej (Assumptions 30).</summary>
    PausedDayChange,

    /// <summary>W zakresie [początek, koniec] nie wypada żaden termin efektywny.</summary>
    NoDueInRange,
}

/// <summary>Wartość pola objętego audytem.</summary>
public sealed record RecurringFieldValue(RecurringDefinitionField Field, object? Value);

/// <summary>Zmienione pole: wartość stara i nowa.</summary>
public sealed record RecurringFieldChange(RecurringDefinitionField Field, object? OldValue, object? NewValue);

/// <summary>Wynik operacji na definicji: zmienione pola albo naruszenie reguły (bez zmian).</summary>
public sealed record RecurringChangeSet(IReadOnlyList<RecurringFieldChange> Changes, RecurringRuleViolation? Violation = null)
{
    public bool Succeeded => Violation is null;

    public static RecurringChangeSet Rejected(RecurringRuleViolation violation) => new([], violation);
}

/// <summary>Naruszenie reguł kwot definicji.</summary>
public enum RecurringAmountError
{
    None,

    /// <summary>Brutto ≤ 0 zł: definicja cykliczna to zawsze koszt (Edge Cases: kwota ≤ 0 odrzucana).</summary>
    GrossNotPositive,

    GrossOutOfRange,

    /// <summary>Netto &lt; 0 zł.</summary>
    NetNegative,

    NetOutOfRange,

    /// <summary>Netto &gt; brutto.</summary>
    NetExceedsGross,
}

/// <summary>Granice wartości harmonogramu i reguły kwot definicji.</summary>
public static class RecurringRules
{
    public const int MinDayOfMonth = 1;
    public const int MaxDayOfMonth = 31;

    public static readonly DateOnly MinDate = new(2000, 1, 1);
    public static readonly DateOnly MaxDate = new(2099, 12, 31);

    public static bool IsValidDay(int dayOfMonth) => dayOfMonth is >= MinDayOfMonth and <= MaxDayOfMonth;

    public static bool IsInDateRange(DateOnly date) => date >= MinDate && date <= MaxDate;

    /// <summary>Brutto &gt; 0 zł; netto opcjonalne, 0 ≤ netto ≤ brutto.</summary>
    public static RecurringAmountError CheckAmounts(decimal gross, decimal? net)
    {
        if (!ExpenseAmounts.IsInRange(gross))
        {
            return RecurringAmountError.GrossOutOfRange;
        }

        if (gross <= 0)
        {
            return RecurringAmountError.GrossNotPositive;
        }

        return net switch
        {
            null => RecurringAmountError.None,
            { } value when !ExpenseAmounts.IsInRange(value) => RecurringAmountError.NetOutOfRange,
            < 0 => RecurringAmountError.NetNegative,
            { } value when value > gross => RecurringAmountError.NetExceedsGross,
            _ => RecurringAmountError.None,
        };
    }
}
