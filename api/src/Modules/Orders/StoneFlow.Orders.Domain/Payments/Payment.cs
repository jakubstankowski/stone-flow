namespace StoneFlow.Orders.Domain.Payments;

/// <summary>Dane wpłaty z formularza, już znormalizowane (przycięte, pusty tekst to <c>null</c>).</summary>
public sealed record PaymentDetails(decimal Amount, DateOnly Date, PaymentMethod Method, string? ReceivedBy);

/// <summary>Wpłata: jedyny zapis wpływu pieniędzy do zamówienia. Nie zmienia wartości ani statusu zamówienia.</summary>
public sealed class Payment : OrderEntry
{
    public const int ReceivedByMaxLength = 100;

    private Payment(Guid id, Guid orderId, PaymentDetails details, Guid creationRequestId, byte[] creationRequestHash, DateTime nowUtc)
        : base(id, orderId, details.Amount, details.Date, creationRequestId, creationRequestHash, nowUtc)
    {
        Method = details.Method;
        ReceivedBy = details.ReceivedBy;
    }

    // Dla EF Core.
    private Payment()
    {
    }

    public PaymentMethod Method { get; private set; }

    /// <summary>„Przyjął”: kto przyjął gotówkę; tylko przy <see cref="PaymentMethod.Cash"/>. Dane osobowe.</summary>
    public string? ReceivedBy { get; private set; }

    /// <exception cref="ArgumentException">Dane naruszają reguły, które sprawdza walidacja przed utworzeniem.</exception>
    public static Payment Create(
        Guid id,
        Guid orderId,
        PaymentDetails details,
        Guid creationRequestId,
        byte[] creationRequestHash,
        DateTime nowUtc)
    {
        Require(details);
        return new Payment(id, orderId, details, creationRequestId, creationRequestHash, nowUtc);
    }

    /// <summary>Poprawa wpłaty; zwraca zmienione pola (pusta lista, gdy nic się nie zmieniło).</summary>
    /// <exception cref="ArgumentException">Dane naruszają reguły, które sprawdza walidacja przed poprawą.</exception>
    public IReadOnlyList<EntryFieldChange> Edit(PaymentDetails details, DateTime nowUtc)
    {
        Require(details);
        var before = Snapshot();
        Amount = details.Amount;
        Date = details.Date;
        Method = details.Method;
        ReceivedBy = details.ReceivedBy;
        return Changed(before, nowUtc);
    }

    public override IReadOnlyList<EntryFieldValue> Snapshot() =>
    [
        new(EntryField.Amount, Amount),
        new(EntryField.Date, Date),
        new(EntryField.Method, Method),
        new(EntryField.ReceivedBy, ReceivedBy),
    ];

    private static void Require(PaymentDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        RequireAmount(details.Amount);
        if (details.ReceivedBy is { } receivedBy
            && (details.Method != PaymentMethod.Cash || string.IsNullOrWhiteSpace(receivedBy) || receivedBy.Length > ReceivedByMaxLength))
        {
            throw new ArgumentException("„Przyjął” podaje się tylko przy gotówce, najwyżej 100 znaków.", nameof(details));
        }
    }
}
