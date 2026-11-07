namespace StoneFlow.Expenses.Domain.Expenses;

/// <summary>Naruszenie reguł kwot wydatku.</summary>
public enum ExpenseAmountError
{
    None,

    /// <summary>Kwota brutto 0 zł (WYD-01, 02).</summary>
    GrossZero,

    /// <summary>Kwota poza zakresem <c>decimal(18,2)</c>.</summary>
    GrossOutOfRange,

    /// <summary>Kwota netto poza zakresem <c>decimal(18,2)</c>.</summary>
    NetOutOfRange,

    /// <summary>Niezerowe netto ma inny znak niż brutto.</summary>
    NetSign,

    /// <summary>|netto| &gt; |brutto|.</summary>
    NetExceedsGross,
}

/// <summary>
/// Reguły kwot wydatku (PRD §5, §8; decyzja właściciela z 06.11.2026): brutto różne od 0 zł,
/// ujemne oznacza korektę lub zwrot od dostawcy; netto opcjonalne, 0,00 zł dozwolone, niezerowe
/// netto ma znak brutto, a |netto| ≤ |brutto|. System nie wylicza kwot ze stawki.
/// </summary>
public static class ExpenseAmounts
{
    /// <summary>Największa wartość bezwzględna mieszcząca się w <c>decimal(18,2)</c>.</summary>
    public const decimal MaxAbsolute = 9_999_999_999_999_999.99m;

    public static bool IsInRange(decimal amount) => Math.Abs(amount) <= MaxAbsolute;

    /// <summary>Pierwsze naruszenie brutto (<see cref="GrossErrorOf"/>) albo netto względem brutto.</summary>
    public static ExpenseAmountError Check(decimal gross, decimal? net)
    {
        var grossError = GrossErrorOf(gross);
        return grossError != ExpenseAmountError.None ? grossError : NetErrorOf(gross, net);
    }

    public static ExpenseAmountError GrossErrorOf(decimal gross) =>
        gross == 0 ? ExpenseAmountError.GrossZero
        : !IsInRange(gross) ? ExpenseAmountError.GrossOutOfRange
        : ExpenseAmountError.None;

    /// <summary>Reguła netto przy poprawnym brutto; <c>null</c> (brak netto) jest poprawne.</summary>
    public static ExpenseAmountError NetErrorOf(decimal gross, decimal? net)
    {
        if (net is not { } value)
        {
            return ExpenseAmountError.None;
        }

        if (!IsInRange(value))
        {
            return ExpenseAmountError.NetOutOfRange;
        }

        if (value != 0 && Math.Sign(value) != Math.Sign(gross))
        {
            return ExpenseAmountError.NetSign;
        }

        return Math.Abs(value) > Math.Abs(gross) ? ExpenseAmountError.NetExceedsGross : ExpenseAmountError.None;
    }
}
