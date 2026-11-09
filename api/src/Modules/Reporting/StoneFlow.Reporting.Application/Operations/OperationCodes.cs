using System.Globalization;
using StoneFlow.Reporting.Domain.CashFlow;
using StoneFlow.Shared.Kernel.Operations;

namespace StoneFlow.Reporting.Application.Operations;

/// <summary>
/// Kody listy „Operacje” w API (jedno miejsce mapowania): rodzaje operacji, kierunki i typy źródła;
/// format kwot (<c>-1234.50</c>) i dat (<c>yyyy-MM-dd</c>) w kulturze niezmiennej. Kody są
/// porównywane dokładnie.
/// </summary>
public static class OperationCodes
{
    private static readonly Dictionary<CashOperationKind, string> Kinds = new()
    {
        [CashOperationKind.Payment] = "payment",
        [CashOperationKind.Refund] = "refund",
        [CashOperationKind.PaidExpense] = "expense",
        [CashOperationKind.OwnerWithdrawal] = "withdrawal",
    };

    private static readonly Dictionary<CashDirection, string> Directions = new()
    {
        [CashDirection.Inflow] = "inflow",
        [CashDirection.Outflow] = "outflow",
    };

    public static string Of(CashOperationKind kind) => Kinds[kind];

    public static string Of(CashDirection direction) => Directions[direction];

    /// <summary>Typ obiektu źródłowego do linku: zamówienie dla wpłat i zwrotów, wydatek, wypłata.</summary>
    public static string SourceTypeOf(CashOperationKind kind) => kind switch
    {
        CashOperationKind.Payment or CashOperationKind.Refund => "order",
        CashOperationKind.PaidExpense => "expense",
        CashOperationKind.OwnerWithdrawal => "withdrawal",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Nieznany rodzaj operacji."),
    };

    public static bool TryParse(string? code, out CashOperationKind kind)
    {
        foreach (var (key, text) in Kinds)
        {
            if (string.Equals(text, code, StringComparison.Ordinal))
            {
                kind = key;
                return true;
            }
        }

        kind = default;
        return false;
    }

    public static string FormatAmount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static bool TryParseDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
