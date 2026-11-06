namespace StoneFlow.Integrations.Domain.WooCommerce;

/// <summary>Stabilne kody statusu błędu zamówienia (baza i API).</summary>
public static class OrderErrorStatus
{
    public const string Open = "open";
    public const string Closed = "closed";
}

/// <summary>Stabilne kody źródła błędu i źródła zamknięcia (baza i API).</summary>
public static class OrderErrorSource
{
    public const string Backfill = "backfill";
    public const string Webhook = "webhook";
    public const string Retry = "retry";
}

/// <summary>
/// Reguły historii błędów zamówień WooCommerce (SF-13, „Historia błędów zamówień”): limity pól
/// i warunek zamknięcia otwartego błędu udanym zapisem.
/// </summary>
public static class OrderErrorRules
{
    public const int MaxExternalIdLength = 64;
    public const int MaxMessageLength = 1000;
    public const int MaxContextLength = 200;

    /// <summary>
    /// Udany zapis zamyka błąd, gdy dostawa nie jest starsza od nieudanej: nieudana bez daty
    /// modyfikacji albo udana z datą nie wcześniejszą. Usunięcie w sklepie zamyka zawsze.
    /// </summary>
    /// <param name="failedModifiedAtUtc">Data modyfikacji nieudanej dostawy (największa z prób).</param>
    /// <param name="successModifiedAtUtc">Data modyfikacji udanej dostawy.</param>
    /// <param name="isDeletion">Czy udana dostawa to usunięcie zamówienia w sklepie.</param>
    public static bool CanClose(DateTime? failedModifiedAtUtc, DateTime? successModifiedAtUtc, bool isDeletion) =>
        isDeletion
        || failedModifiedAtUtc is null
        || (successModifiedAtUtc is { } success && success >= failedModifiedAtUtc.Value);

    /// <summary>Data modyfikacji po kolejnej nieudanej próbie: większa z dwóch wartości.</summary>
    public static DateTime? LaterModified(DateTime? current, DateTime? next) =>
        current is { } a && next is { } b ? (a >= b ? a : b) : current ?? next;

    /// <summary>Tekst przycięty do limitu pola.</summary>
    public static string Clip(string text, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}
