using System.Globalization;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Reporting.Application.ChangeHistory;

/// <summary>
/// Kody listy „Historia zmian” w API (jedno miejsce mapowania): rodzaje obiektu, operacje i autorzy;
/// format dat (<c>yyyy-MM-dd</c>) i czasu (ISO 8601 UTC z „Z”) w kulturze niezmiennej. Kody są
/// porównywane dokładnie.
/// </summary>
public static class ChangeHistoryCodes
{
    private static readonly Dictionary<AuditObjectType, string> ObjectTypes = new()
    {
        [AuditObjectType.Order] = "order",
        [AuditObjectType.Payment] = "payment",
        [AuditObjectType.Refund] = "refund",
        [AuditObjectType.SalesCorrection] = "salesCorrection",
        [AuditObjectType.Expense] = "expense",
        [AuditObjectType.RecurringExpenseDefinition] = "recurringExpenseDefinition",
        [AuditObjectType.OwnerWithdrawal] = "ownerWithdrawal",
    };

    private static readonly Dictionary<AuditOperation, string> Operations = new()
    {
        [AuditOperation.Created] = "created",
        [AuditOperation.Updated] = "updated",
        [AuditOperation.Deleted] = "deleted",
    };

    private static readonly Dictionary<AuditAuthorKind, string> Authors = new()
    {
        [AuditAuthorKind.Account] = "account",
        [AuditAuthorKind.WooCommerceSync] = "wooCommerceSync",
        [AuditAuthorKind.AutomatedJob] = "automatedJob",
    };

    public static string Of(AuditObjectType type) => ObjectTypes[type];

    public static string Of(AuditOperation operation) => Operations[operation];

    public static string Of(AuditAuthorKind author) => Authors[author];

    public static bool TryParse(string? code, out AuditObjectType type)
    {
        foreach (var (key, text) in ObjectTypes)
        {
            if (string.Equals(text, code, StringComparison.Ordinal))
            {
                type = key;
                return true;
            }
        }

        type = default;
        return false;
    }

    public static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static bool TryParseDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>Czas UTC w formacie „O” z „Z” (np. <c>2026-11-05T12:00:00.0000000Z</c>), niezależnie od <see cref="DateTime.Kind"/> wejścia.</summary>
    public static string FormatTimestamp(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
}
