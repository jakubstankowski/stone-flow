using StoneFlow.Orders.Domain.Orders;

namespace StoneFlow.Orders.Domain.Payments;

/// <summary>
/// Wpis pieniężny zamówienia: wpłata albo zwrot. Osobny obiekt powiązany z zamówieniem przez
/// <see cref="OrderId"/>, a nie część agregatu <see cref="Order"/>; granicą spójności sum jest
/// blokada wiersza zamówienia w jednostce pracy. Usuwanie jest fizyczne, a ślad zostaje w audycie.
/// </summary>
public abstract class OrderEntry
{
    protected OrderEntry(Guid id, Guid orderId, decimal amount, DateOnly date, Guid creationRequestId, byte[] creationRequestHash, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(creationRequestHash);
        if (id == Guid.Empty || orderId == Guid.Empty)
        {
            throw new ArgumentException("Brak identyfikatora wpisu albo zamówienia.", nameof(id));
        }

        RequireAmount(amount);
        Id = id;
        OrderId = orderId;
        Amount = amount;
        Date = date;
        CreationRequestId = creationRequestId;
        CreationRequestHash = creationRequestHash;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    // Dla EF Core.
    protected OrderEntry()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    /// <summary>Kwota w złotych, zawsze dodatnia.</summary>
    public decimal Amount { get; protected set; }

    /// <summary>Data operacji (data biznesowa wpisu).</summary>
    public DateOnly Date { get; protected set; }

    /// <summary>Klucz idempotencji żądania, które utworzyło wpis.</summary>
    public Guid? CreationRequestId { get; private set; }

    /// <summary>SHA-256 znormalizowanej komendy dodania (32 bajty), razem z identyfikatorem zamówienia.</summary>
    public byte[]? CreationRequestHash { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Wartości wszystkich pól objętych audytem, w stałej kolejności.</summary>
    public abstract IReadOnlyList<EntryFieldValue> Snapshot();

    /// <exception cref="ArgumentException">Kwota nie jest dodatnia albo nie mieści się w <c>decimal(18,2)</c>.</exception>
    protected static void RequireAmount(decimal amount)
    {
        if (amount is <= 0 or > Order.MaxValue)
        {
            throw new ArgumentException("Kwota wpisu musi być dodatnia i mieścić się w zakresie.", nameof(amount));
        }
    }

    /// <summary>
    /// Zastępuje skrót żądania utworzenia zerami (SF-22): skrót liczony z treści (np. z powodem) pozwalałby
    /// potwierdzić zgadywane dane. Ponowienie starego żądania dostaje wtedy „klucz użyty z innymi danymi”.
    /// </summary>
    protected void EraseCreationRequestHash()
    {
        if (CreationRequestHash is not null)
        {
            CreationRequestHash = new byte[32];
        }
    }

    /// <summary>Zmienione pola względem <paramref name="before"/>; przy zmianie aktualizuje czas zmiany.</summary>
    protected IReadOnlyList<EntryFieldChange> Changed(IReadOnlyList<EntryFieldValue> before, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(before);

        var after = Snapshot();
        var changes = new List<EntryFieldChange>();
        for (var index = 0; index < before.Count; index++)
        {
            if (!Equals(before[index].Value, after[index].Value))
            {
                changes.Add(new EntryFieldChange(before[index].Field, before[index].Value, after[index].Value));
            }
        }

        if (changes.Count > 0)
        {
            UpdatedAtUtc = nowUtc;
        }

        return changes;
    }
}
