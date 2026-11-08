using System.Globalization;
using StoneFlow.Expenses.Domain.Recurring;

namespace StoneFlow.Expenses.Application.Recurring;

/// <summary>Stabilne kody definicji cyklicznej w API i audycie: stan, pola audytu i okres <c>yyyy-MM</c>.</summary>
public static class RecurringCodes
{
    private static readonly Dictionary<RecurringDefinitionStatus, string> Statuses = new()
    {
        [RecurringDefinitionStatus.Active] = "active",
        [RecurringDefinitionStatus.Paused] = "paused",
        [RecurringDefinitionStatus.Ended] = "ended",
    };

    public static string Of(RecurringDefinitionStatus status) => Statuses[status];

    /// <summary>Nazwa pola w audycie: camelCase nazwy pola (<c>dayOfMonth</c>).</summary>
    public static string Of(RecurringDefinitionField field) => string.Concat(field.ToString()[..1].ToLowerInvariant(), field.ToString().AsSpan(1));

    public static bool TryParse(string? code, out RecurringDefinitionStatus status)
    {
        foreach (var (key, text) in Statuses)
        {
            if (string.Equals(text, code, StringComparison.Ordinal))
            {
                status = key;
                return true;
            }
        }

        status = default;
        return false;
    }

    /// <summary>Okres w API: <c>yyyy-MM</c>.</summary>
    public static string FormatPeriod(DateOnly firstDay) => firstDay.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
