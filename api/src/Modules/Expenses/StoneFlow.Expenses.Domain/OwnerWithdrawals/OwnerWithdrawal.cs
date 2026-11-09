using StoneFlow.Expenses.Domain.Expenses;

namespace StoneFlow.Expenses.Domain.OwnerWithdrawals;

/// <summary>Treść wypłaty podawana przy dodaniu i poprawie (wartości już znormalizowane).</summary>
public sealed record OwnerWithdrawalContent(string Person, decimal Amount, DateOnly WithdrawnOn, OwnerWithdrawalMethod Method);

/// <summary>
/// Wypłata właściciela (PRD §5): wypływ pieniędzy w okresie daty wypłaty. Nie jest wydatkiem ani
/// kosztem i nie zmienia sprzedaży. Encja pilnuje reguły kwoty (&gt; 0 zł) i wylicza zmienione pola
/// na potrzeby audytu. „Dzisiaj” dla daty wypłaty sprawdza walidacja wejścia.
/// </summary>
public sealed class OwnerWithdrawal
{
    public const int PersonMaxLength = 100;

    private OwnerWithdrawal(Guid id, OwnerWithdrawalContent content, Guid creationRequestId, byte[] creationRequestHash, DateTime nowUtc)
    {
        Id = id;
        Person = string.Empty;
        Apply(content);
        CreationRequestId = creationRequestId;
        CreationRequestHash = creationRequestHash;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    // Dla EF Core.
    private OwnerWithdrawal()
    {
        Person = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>Osoba, która wypłaciła pieniądze (dane osobowe).</summary>
    public string Person { get; private set; }

    /// <summary>Kwota w złotych, zawsze dodatnia.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Data wypłaty: okres wypływu.</summary>
    public DateOnly WithdrawnOn { get; private set; }

    public OwnerWithdrawalMethod Method { get; private set; }

    /// <summary>Klucz idempotencji żądania, które utworzyło wypłatę.</summary>
    public Guid? CreationRequestId { get; private set; }

    /// <summary>SHA-256 znormalizowanej komendy dodania (32 bajty).</summary>
    public byte[]? CreationRequestHash { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Reguła kwoty: większa od 0 zł i w zakresie <c>decimal(18,2)</c> (jak wydatek).</summary>
    public static OwnerWithdrawalAmountError AmountErrorOf(decimal amount) =>
        amount <= 0 ? OwnerWithdrawalAmountError.NotPositive
        : !ExpenseAmounts.IsInRange(amount) ? OwnerWithdrawalAmountError.OutOfRange
        : OwnerWithdrawalAmountError.None;

    /// <exception cref="ArgumentException">Brak identyfikatora albo klucza, albo treść narusza reguły sprawdzane przez walidację.</exception>
    public static OwnerWithdrawal Create(Guid id, OwnerWithdrawalContent content, Guid creationRequestId, byte[] creationRequestHash, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(creationRequestHash);
        if (id == Guid.Empty || creationRequestId == Guid.Empty)
        {
            throw new ArgumentException("Brak identyfikatora wypłaty albo klucza żądania.", nameof(id));
        }

        Require(content);
        return new OwnerWithdrawal(id, content, creationRequestId, creationRequestHash, nowUtc);
    }

    /// <summary>Poprawa wszystkich pól; zwraca zmienione pola (pusta lista, gdy nic się nie zmieniło — czas zmiany zostaje).</summary>
    /// <exception cref="ArgumentException">Treść narusza reguły sprawdzane przez walidację.</exception>
    public IReadOnlyList<OwnerWithdrawalFieldChange> Edit(OwnerWithdrawalContent content, DateTime nowUtc)
    {
        Require(content);
        var before = Snapshot();
        Apply(content);
        var after = Snapshot();

        var changes = new List<OwnerWithdrawalFieldChange>();
        for (var index = 0; index < before.Count; index++)
        {
            if (!Equals(before[index].Value, after[index].Value))
            {
                changes.Add(new OwnerWithdrawalFieldChange(before[index].Field, before[index].Value, after[index].Value));
            }
        }

        if (changes.Count > 0)
        {
            UpdatedAtUtc = nowUtc;
        }

        return changes;
    }

    /// <summary>Wartości wszystkich pól objętych audytem, w stałej kolejności.</summary>
    public IReadOnlyList<OwnerWithdrawalFieldValue> Snapshot() =>
    [
        new(OwnerWithdrawalField.Person, Person),
        new(OwnerWithdrawalField.Amount, Amount),
        new(OwnerWithdrawalField.WithdrawnOn, WithdrawnOn),
        new(OwnerWithdrawalField.Method, Method),
    ];

    private static void Require(OwnerWithdrawalContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrWhiteSpace(content.Person) || content.Person.Length > PersonMaxLength)
        {
            throw new ArgumentException("Osoba jest wymagana, najwyżej 100 znaków.", nameof(content));
        }

        if (AmountErrorOf(content.Amount) != OwnerWithdrawalAmountError.None)
        {
            throw new ArgumentException("Kwota wypłaty musi być dodatnia i mieścić się w zakresie.", nameof(content));
        }
    }

    private void Apply(OwnerWithdrawalContent content)
    {
        Person = content.Person;
        Amount = content.Amount;
        WithdrawnOn = content.WithdrawnOn;
        Method = content.Method;
    }
}
