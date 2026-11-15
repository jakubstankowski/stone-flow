namespace StoneFlow.Orders.Domain.Orders;

/// <summary>Stan akcji „Anonimizuj dane klienta” (SF-22).</summary>
public enum AnonymizationStatus
{
    /// <summary>Brak okresu retencji w konfiguracji: funkcja wyłączona (D-09).</summary>
    Disabled,

    /// <summary>Okres retencji jeszcze nie upłynął.</summary>
    RetentionPending,

    /// <summary>Akcja dostępna.</summary>
    Available,

    /// <summary>Dane klienta zanonimizowano (niezależnie od konfiguracji).</summary>
    Anonymized,
}

/// <summary>
/// Dostępność akcji: stan oraz — dla <see cref="AnonymizationStatus.RetentionPending"/> i
/// <see cref="AnonymizationStatus.Available"/> — początek okresu retencji i pierwszy dzień dostępności.
/// </summary>
public sealed record AnonymizationAvailability(AnonymizationStatus Status, DateOnly? RetentionStartDate, DateOnly? AvailableFrom);

/// <summary>
/// Reguły anonimizacji danych klienta (PRD §5, DANE-01, SF-22): znacznik, początek okresu retencji i
/// dostępność akcji. Długość okresu (D-09) przychodzi z konfiguracji; kod nie zakłada wartości domyślnej.
/// </summary>
public static class CustomerAnonymization
{
    /// <summary>Znacznik zastępujący wartości wymagane (nazwa klienta, powód) i wartości w audycie.</summary>
    public const string Marker = "[zanonimizowano]";

    public const int MinRetentionMonths = 1;

    public const int MaxRetentionMonths = 1200;

    /// <summary>
    /// Początek okresu retencji: najpóźniejsza z dat — zamówienia, najpóźniejszego wpisu (wpłata, zwrot,
    /// korekta sprzedaży) i ostatniej zmiany zamówienia lub jego wpisów w Europe/Warsaw (w tym usunięcia
    /// wpisu, odczytanego z audytu). Jedyne miejsce tej reguły: jeżeli rozstrzygnięcie D-09 liczy okres
    /// inaczej (np. od końca roku obrotowego), zmienia się tylko ta metoda (procedura w
    /// <c>docs/operations/anonimizacja.md</c>).
    /// </summary>
    public static DateOnly RetentionStart(DateOnly orderDate, DateOnly? lastEntryDate, DateOnly lastChangeDate)
    {
        var start = orderDate > lastChangeDate ? orderDate : lastChangeDate;
        return lastEntryDate is { } entryDate && entryDate > start ? entryDate : start;
    }

    /// <summary>
    /// Pierwszy dzień dostępności: początek okresu + <paramref name="retentionMonths"/> miesięcy
    /// kalendarzowych (31.01 + 1 miesiąc = ostatni dzień lutego).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Liczba miesięcy spoza 1–1200.</exception>
    public static DateOnly AvailableFrom(DateOnly retentionStart, int retentionMonths)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retentionMonths, MinRetentionMonths);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(retentionMonths, MaxRetentionMonths);
        return retentionStart.AddMonths(retentionMonths);
    }

    /// <summary>
    /// Stan akcji w kolejności: zanonimizowane, wyłączona (brak okresu), przed upływem okresu
    /// (<paramref name="today"/> &lt; dostępne od), dostępna.
    /// </summary>
    /// <param name="isAnonymized">Zamówienie jest już zanonimizowane.</param>
    /// <param name="retentionMonths">Okres retencji z konfiguracji; <c>null</c> = funkcja wyłączona.</param>
    /// <param name="retentionStart">Początek okresu; wymagany, gdy funkcja jest włączona, a zamówienie niezanonimizowane.</param>
    /// <param name="today">Dzisiejsza data w Europe/Warsaw.</param>
    /// <exception cref="ArgumentException">Funkcja włączona bez początku okresu.</exception>
    public static AnonymizationAvailability Evaluate(bool isAnonymized, int? retentionMonths, DateOnly? retentionStart, DateOnly today)
    {
        if (isAnonymized)
        {
            return new AnonymizationAvailability(AnonymizationStatus.Anonymized, null, null);
        }

        if (retentionMonths is not { } months)
        {
            return new AnonymizationAvailability(AnonymizationStatus.Disabled, null, null);
        }

        if (retentionStart is not { } start)
        {
            throw new ArgumentException("Włączona anonimizacja wymaga początku okresu retencji.", nameof(retentionStart));
        }

        var availableFrom = AvailableFrom(start, months);
        var status = today < availableFrom ? AnonymizationStatus.RetentionPending : AnonymizationStatus.Available;
        return new AnonymizationAvailability(status, start, availableFrom);
    }
}
