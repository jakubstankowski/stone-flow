using StoneFlow.Expenses.Domain.Expenses;

namespace StoneFlow.Expenses.Application;

/// <summary>
/// Stabilne kody słowników (camelCase) używane w API i w audycie. Frontend mapuje je na polskie
/// etykiety. Kody są porównywane dokładnie (z rozróżnieniem wielkości liter).
/// </summary>
public static class ExpenseCodes
{
    private static readonly Dictionary<ExpenseCategory, string> Categories = new()
    {
        [ExpenseCategory.MaterialPurchase] = "materialPurchase",
        [ExpenseCategory.Cutting] = "cutting",
        [ExpenseCategory.HdsTransport] = "hdsTransport",
        [ExpenseCategory.Marketing] = "marketing",
        [ExpenseCategory.OtherOperations] = "otherOperations",
        [ExpenseCategory.VatTax] = "vatTax",
        [ExpenseCategory.PpeTax] = "ppeTax",
    };

    /// <summary>Etykiety kategorii do etykiety obiektu audytu („Wydatek – Cięcie”).</summary>
    private static readonly Dictionary<ExpenseCategory, string> CategoryLabels = new()
    {
        [ExpenseCategory.MaterialPurchase] = "Zakup materiału u dostawców",
        [ExpenseCategory.Cutting] = "Cięcie",
        [ExpenseCategory.HdsTransport] = "Transport HDS",
        [ExpenseCategory.Marketing] = "Marketing",
        [ExpenseCategory.OtherOperations] = "Pozostała działalność",
        [ExpenseCategory.VatTax] = "Podatek VAT",
        [ExpenseCategory.PpeTax] = "Podatek PPE",
    };

    private static readonly Dictionary<ExpensePaymentMethod, string> Methods = new()
    {
        [ExpensePaymentMethod.Transfer] = "transfer",
        [ExpensePaymentMethod.Blik] = "blik",
        [ExpensePaymentMethod.Cash] = "cash",
        [ExpensePaymentMethod.CashOnDelivery] = "cashOnDelivery",
    };

    private static readonly Dictionary<ExpensePaymentStatus, string> Statuses = new()
    {
        [ExpensePaymentStatus.Planned] = "planned",
        [ExpensePaymentStatus.Paid] = "paid",
    };

    public static string Of(ExpenseCategory category) => Categories[category];

    public static string LabelOf(ExpenseCategory category) => CategoryLabels[category];

    public static string Of(ExpensePaymentMethod method) => Methods[method];

    public static string Of(ExpensePaymentStatus status) => Statuses[status];

    /// <summary>Nazwa pola w audycie: camelCase nazwy pola (<c>grossAmount</c>).</summary>
    public static string Of(ExpenseField field) => string.Concat(field.ToString()[..1].ToLowerInvariant(), field.ToString().AsSpan(1));

    public static bool TryParse(string? code, out ExpenseCategory category) => TryParse(Categories, code, out category);

    public static bool TryParse(string? code, out ExpensePaymentMethod method) => TryParse(Methods, code, out method);

    private static bool TryParse<TEnum>(Dictionary<TEnum, string> codes, string? code, out TEnum value)
        where TEnum : struct, Enum
    {
        foreach (var (key, text) in codes)
        {
            if (string.Equals(text, code, StringComparison.Ordinal))
            {
                value = key;
                return true;
            }
        }

        value = default;
        return false;
    }
}
