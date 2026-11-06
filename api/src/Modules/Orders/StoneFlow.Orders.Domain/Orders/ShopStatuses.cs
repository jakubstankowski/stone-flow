namespace StoneFlow.Orders.Domain.Orders;

/// <summary>Rodzaj dostawy ze sklepu (temat webhooka WooCommerce).</summary>
public enum ShopDelivery
{
    Created,
    Updated,
    Restored,
    Deleted,
}

/// <summary>Ostrzeżenia ze sklepu pokazywane w szczegółach zamówienia (SF-12).</summary>
public enum ShopWarning
{
    /// <summary>„Zamówienie usunięte w sklepie”.</summary>
    DeletedInShop,

    /// <summary>„Zamówienie aktywne w sklepie” — anulowane w ERP, aktywne w sklepie.</summary>
    ActiveInShop,

    /// <summary>„Zamówienie anulowane w sklepie” — aktywne w ERP, zakończone w sklepie.</summary>
    CancelledInShop,

    /// <summary>„Zwrot w sklepie” — informacja, nie tworzy wypływu.</summary>
    RefundedInShop,
}

/// <summary>Statusy WooCommerce i wynikające z nich reguły ERP (PRD sekcja 5, SF-12).</summary>
public static class ShopStatuses
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string OnHold = "on-hold";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Refunded = "refunded";
    public const string Failed = "failed";
    public const string Trash = "trash";
    public const string CheckoutDraft = "checkout-draft";

    /// <summary>Statusy ukryte domyślnie na liście: nie liczą się do sprzedaży do zmiany w sklepie.</summary>
    public static readonly IReadOnlyList<string> HiddenByDefault = [Pending, Failed, CheckoutDraft];

    private static readonly HashSet<string> Known =
        new([Pending, Processing, OnHold, Completed, Cancelled, Refunded, Failed, Trash, CheckoutDraft], StringComparer.Ordinal);

    public static bool IsKnown(string? status) => status is not null && Known.Contains(status);

    public static bool IsHiddenByDefault(string? status) => status is not null && HiddenByDefault.Contains(status, StringComparer.Ordinal);

    /// <summary>
    /// „Zakończone w sklepie”: usunięte albo ze statusem anulowanym lub w koszu. Nieznany status
    /// traktowany jak aktywny.
    /// </summary>
    public static bool IsTerminal(string? status, DateTime? deletedAtUtc) =>
        deletedAtUtc is not null || status is Cancelled or Trash;

    /// <summary>Ostrzeżenia ze sklepu dla szczegółów zamówienia, w stałej kolejności.</summary>
    public static IReadOnlyList<ShopWarning> WarningsFor(
        OrderSource source,
        FulfillmentStatus fulfillment,
        string? status,
        DateTime? deletedAtUtc,
        decimal refundedAmount)
    {
        if (source != OrderSource.WooCommerce)
        {
            return [];
        }

        var warnings = new List<ShopWarning>();
        var terminal = IsTerminal(status, deletedAtUtc);
        var cancelledInErp = fulfillment == FulfillmentStatus.Cancelled;
        if (deletedAtUtc is not null || status == Trash)
        {
            warnings.Add(ShopWarning.DeletedInShop);
        }

        if (cancelledInErp && !terminal)
        {
            warnings.Add(ShopWarning.ActiveInShop);
        }

        if (!cancelledInErp && terminal)
        {
            warnings.Add(ShopWarning.CancelledInShop);
        }

        if (refundedAmount > 0)
        {
            warnings.Add(ShopWarning.RefundedInShop);
        }

        return warnings;
    }
}

/// <summary>Dane zamówienia z dostawy ze sklepu (pełne zamówienie: utworzenie, zmiana, przywrócenie).</summary>
public sealed record ShopOrderData(
    DateTime ModifiedAtUtc,
    DateOnly OrderDate,
    string Status,
    decimal Value,
    decimal RefundedAmount,
    Customer Customer);

/// <summary>Powód pominięcia dostawy ze sklepu.</summary>
public enum ShopDeliverySkip
{
    /// <summary>Dostawa starsza od zapisanych danych (data modyfikacji w sklepie).</summary>
    Older,

    /// <summary>Spóźniona dostawa sprzed usunięcia zamówienia w sklepie.</summary>
    BeforeDeletion,
}

/// <summary>
/// Wynik zastosowania dostawy: zmienione pola audytowane, czy zmienił się stan zapisywany (także
/// sama data modyfikacji w sklepie) albo powód pominięcia.
/// </summary>
public sealed record ShopApplyResult(IReadOnlyList<OrderFieldChange> Changes, bool StateChanged, ShopDeliverySkip? Skipped)
{
    public static ShopApplyResult Skip(ShopDeliverySkip reason) => new([], false, reason);
}
