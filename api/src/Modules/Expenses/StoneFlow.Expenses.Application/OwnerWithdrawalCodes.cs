using StoneFlow.Expenses.Domain.OwnerWithdrawals;

namespace StoneFlow.Expenses.Application;

/// <summary>
/// Stabilne kody wypłaty (camelCase) w API i w audycie; porównywane dokładnie. Kody metod są te
/// same co przy zwrotach i wydatkach (<c>transfer</c>, <c>blik</c>, <c>cash</c>).
/// </summary>
public static class OwnerWithdrawalCodes
{
    private static readonly Dictionary<OwnerWithdrawalMethod, string> Methods = new()
    {
        [OwnerWithdrawalMethod.Transfer] = "transfer",
        [OwnerWithdrawalMethod.Blik] = "blik",
        [OwnerWithdrawalMethod.Cash] = "cash",
    };

    public static string Of(OwnerWithdrawalMethod method) => Methods[method];

    /// <summary>Nazwa pola w audycie: camelCase nazwy pola (<c>withdrawnOn</c>).</summary>
    public static string Of(OwnerWithdrawalField field) => string.Concat(field.ToString()[..1].ToLowerInvariant(), field.ToString().AsSpan(1));

    public static bool TryParse(string? code, out OwnerWithdrawalMethod method)
    {
        foreach (var (key, text) in Methods)
        {
            if (string.Equals(text, code, StringComparison.Ordinal))
            {
                method = key;
                return true;
            }
        }

        method = default;
        return false;
    }
}
