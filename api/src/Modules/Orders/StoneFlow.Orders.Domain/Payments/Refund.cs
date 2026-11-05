namespace StoneFlow.Orders.Domain.Payments;

/// <summary>Dane zwrotu z formularza, już znormalizowane (powód przycięty).</summary>
public sealed record RefundDetails(RefundKind Kind, decimal Amount, DateOnly Date, RefundMethod Method, string Reason)
{
    /// <summary>Wkład w obniżenia sprzedaży (<see cref="Refund.SalesReductionOf"/>).</summary>
    public decimal SalesReduction => Refund.SalesReductionOf(Kind, Amount);
}

/// <summary>
/// Zwrot pieniędzy klientowi. Rodzaj można zmienić poprawką (z audytem); zwrot sprzedaży wchodzi
/// do obniżeń sprzedaży i podlega ich limitowi (SF-10).
/// </summary>
public sealed class Refund : OrderEntry
{
    public const int ReasonMaxLength = 500;

    private Refund(Guid id, Guid orderId, RefundDetails details, Guid creationRequestId, byte[] creationRequestHash, DateTime nowUtc)
        : base(id, orderId, details.Amount, details.Date, creationRequestId, creationRequestHash, nowUtc)
    {
        Kind = details.Kind;
        Method = details.Method;
        Reason = details.Reason;
    }

    // Dla EF Core.
    private Refund()
    {
        Reason = string.Empty;
    }

    public RefundKind Kind { get; private set; }

    public RefundMethod Method { get; private set; }

    public string Reason { get; private set; }

    /// <summary>Wkład w obniżenia sprzedaży (<see cref="SalesReductionOf"/>).</summary>
    public decimal SalesReduction => SalesReductionOf(Kind, Amount);

    /// <summary>Wkład zwrotu w obniżenia sprzedaży: kwota zwrotu sprzedaży, 0 dla zwrotu pieniędzy.</summary>
    public static decimal SalesReductionOf(RefundKind kind, decimal amount) => kind == RefundKind.SalesReturn ? amount : 0;

    /// <exception cref="ArgumentException">Dane naruszają reguły, które sprawdza walidacja przed utworzeniem.</exception>
    public static Refund Create(
        Guid id,
        Guid orderId,
        RefundDetails details,
        Guid creationRequestId,
        byte[] creationRequestHash,
        DateTime nowUtc)
    {
        Require(details);
        return new Refund(id, orderId, details, creationRequestId, creationRequestHash, nowUtc);
    }

    /// <summary>Poprawa zwrotu, także rodzaju; zwraca zmienione pola (pusta lista, gdy nic się nie zmieniło).</summary>
    /// <exception cref="ArgumentException">Dane naruszają reguły, które sprawdza walidacja przed poprawą.</exception>
    public IReadOnlyList<EntryFieldChange> Edit(RefundDetails details, DateTime nowUtc)
    {
        Require(details);
        var before = Snapshot();
        Kind = details.Kind;
        Amount = details.Amount;
        Date = details.Date;
        Method = details.Method;
        Reason = details.Reason;
        return Changed(before, nowUtc);
    }

    public override IReadOnlyList<EntryFieldValue> Snapshot() =>
    [
        new(EntryField.Kind, Kind),
        new(EntryField.Amount, Amount),
        new(EntryField.Date, Date),
        new(EntryField.Method, Method),
        new(EntryField.Reason, Reason),
    ];

    private static void Require(RefundDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        RequireAmount(details.Amount);
        if (string.IsNullOrWhiteSpace(details.Reason) || details.Reason.Length > ReasonMaxLength)
        {
            throw new ArgumentException("Powód zwrotu jest wymagany, najwyżej 500 znaków.", nameof(details));
        }
    }
}
