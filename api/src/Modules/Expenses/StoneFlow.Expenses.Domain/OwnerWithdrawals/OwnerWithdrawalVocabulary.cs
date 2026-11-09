namespace StoneFlow.Expenses.Domain.OwnerWithdrawals;

/// <summary>Metoda wypłaty właściciela (jak przy zwrotach; bez pobrania).</summary>
public enum OwnerWithdrawalMethod
{
    /// <summary>„Przelew”.</summary>
    Transfer,

    /// <summary>„BLIK”.</summary>
    Blik,

    /// <summary>„Gotówka”.</summary>
    Cash,
}

/// <summary>Pola wypłaty objęte audytem (kolejność jak w zapisie audytu).</summary>
public enum OwnerWithdrawalField
{
    Person,
    Amount,
    WithdrawnOn,
    Method,
}

/// <summary>Wartość pola wypłaty objętego audytem.</summary>
public sealed record OwnerWithdrawalFieldValue(OwnerWithdrawalField Field, object Value);

/// <summary>Zmienione pole wypłaty: wartość stara i nowa.</summary>
public sealed record OwnerWithdrawalFieldChange(OwnerWithdrawalField Field, object OldValue, object NewValue);

/// <summary>Naruszenie reguły kwoty wypłaty.</summary>
public enum OwnerWithdrawalAmountError
{
    None,

    /// <summary>Kwota 0 zł albo ujemna (wpłaty właściciela są poza MVP).</summary>
    NotPositive,

    /// <summary>Kwota poza zakresem <c>decimal(18,2)</c>.</summary>
    OutOfRange,
}
