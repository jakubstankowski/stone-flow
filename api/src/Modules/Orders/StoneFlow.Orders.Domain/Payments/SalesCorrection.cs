using StoneFlow.Orders.Domain.Orders;

namespace StoneFlow.Orders.Domain.Payments;

/// <summary>Dane korekty sprzedaży z formularza, już znormalizowane (powód przycięty).</summary>
public sealed record SalesCorrectionDetails(decimal Amount, DateOnly Date, string Reason);

/// <summary>
/// Korekta sprzedaży: oddany towar bez zwrotu pieniędzy. Obniża sprzedaż (raporty, SF-19), ale nie
/// jest operacją pieniężną: nie ma metody i nie zmienia Zapłacono, stanu płatności ani wartości
/// zamówienia. Jej kwota w całości wchodzi do obniżeń sprzedaży.
/// </summary>
public sealed class SalesCorrection : OrderEntry
{
    public const int ReasonMaxLength = 500;

    private SalesCorrection(Guid id, Guid orderId, SalesCorrectionDetails details, Guid creationRequestId, byte[] creationRequestHash, DateTime nowUtc)
        : base(id, orderId, details.Amount, details.Date, creationRequestId, creationRequestHash, nowUtc)
    {
        Reason = details.Reason;
    }

    // Dla EF Core.
    private SalesCorrection()
    {
        Reason = string.Empty;
    }

    public string Reason { get; private set; }

    /// <exception cref="ArgumentException">Dane naruszają reguły, które sprawdza walidacja przed utworzeniem.</exception>
    public static SalesCorrection Create(
        Guid id,
        Guid orderId,
        SalesCorrectionDetails details,
        Guid creationRequestId,
        byte[] creationRequestHash,
        DateTime nowUtc)
    {
        Require(details);
        return new SalesCorrection(id, orderId, details, creationRequestId, creationRequestHash, nowUtc);
    }

    /// <summary>Poprawa korekty; zwraca zmienione pola (pusta lista, gdy nic się nie zmieniło).</summary>
    /// <exception cref="ArgumentException">Dane naruszają reguły, które sprawdza walidacja przed poprawą.</exception>
    public IReadOnlyList<EntryFieldChange> Edit(SalesCorrectionDetails details, DateTime nowUtc)
    {
        Require(details);
        var before = Snapshot();
        Amount = details.Amount;
        Date = details.Date;
        Reason = details.Reason;
        return Changed(before, nowUtc);
    }

    /// <summary>
    /// Anonimizacja powodu korekty (SF-22, nieodwracalna): powód zastępuje znacznik
    /// <see cref="CustomerAnonymization.Marker"/> (powód jest wymagany), a skrót żądania utworzenia —
    /// zera. Kwota, data i pozostałe pola się nie zmieniają. Zwraca zmienione pola (pusta lista, gdy powód
    /// był już znacznikiem; czas zmiany się wtedy nie zmienia).
    /// </summary>
    public IReadOnlyList<EntryFieldChange> AnonymizeReason(DateTime nowUtc)
    {
        var before = Snapshot();
        Reason = CustomerAnonymization.Marker;
        EraseCreationRequestHash();
        return Changed(before, nowUtc);
    }

    public override IReadOnlyList<EntryFieldValue> Snapshot() =>
    [
        new(EntryField.Amount, Amount),
        new(EntryField.Date, Date),
        new(EntryField.Reason, Reason),
    ];

    private static void Require(SalesCorrectionDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        RequireAmount(details.Amount);
        if (string.IsNullOrWhiteSpace(details.Reason) || details.Reason.Length > ReasonMaxLength)
        {
            throw new ArgumentException("Powód korekty jest wymagany, najwyżej 500 znaków.", nameof(details));
        }
    }
}
