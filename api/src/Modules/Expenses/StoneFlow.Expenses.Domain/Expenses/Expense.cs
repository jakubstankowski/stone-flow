using StoneFlow.Expenses.Domain.Recurring;

namespace StoneFlow.Expenses.Domain.Expenses;

/// <summary>Treść wydatku podawana przy utworzeniu i poprawie (wartości już znormalizowane).</summary>
public sealed record ExpenseContent(
    string Description,
    ExpenseCategory Category,
    string? Counterparty,
    DateOnly CostDate,
    DateOnly? DueDate,
    DateOnly? PaidOn,
    decimal GrossAmount,
    decimal? NetAmount,
    string? DocumentReference,
    ExpensePaymentMethod? PaymentMethod,
    string? Payer,
    string? Comment);

/// <summary>
/// Wydatek (jednorazowy; wystąpienie cykliczne z SF-16 to ten sam obiekt). Stan „Opłacony”
/// wyznacza wyłącznie data zapłaty (<see cref="ExpensePayment"/>). Encja pilnuje reguł kwot
/// i akcji zapłaty oraz wylicza zmienione pola na potrzeby audytu.
/// </summary>
public sealed class Expense
{
    public const int DescriptionMaxLength = 200;
    public const int CounterpartyMaxLength = 200;
    public const int DocumentReferenceMaxLength = 100;
    public const int PayerMaxLength = 100;
    public const int CommentMaxLength = 2000;

    private Expense(Guid id, ExpenseContent content, DateTime nowUtc)
    {
        Id = id;
        Description = string.Empty;
        Apply(content);
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    // Dla EF Core.
    private Expense()
    {
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Description { get; private set; }

    public ExpenseCategory Category { get; private set; }

    public string? Counterparty { get; private set; }

    /// <summary>Data kosztu: okres kosztów (SF-19).</summary>
    public DateOnly CostDate { get; private set; }

    /// <summary>Termin płatności — plan, nie zapłata.</summary>
    public DateOnly? DueDate { get; private set; }

    /// <summary>Data zapłaty: stan „Opłacony” i okres wypływu (SF-17, SF-20).</summary>
    public DateOnly? PaidOn { get; private set; }

    /// <summary>Kwota brutto w złotych, różna od 0; ujemna to korekta lub zwrot od dostawcy.</summary>
    public decimal GrossAmount { get; private set; }

    public decimal? NetAmount { get; private set; }

    public string? DocumentReference { get; private set; }

    public ExpensePaymentMethod? PaymentMethod { get; private set; }

    /// <summary>Kto zapłacił; bez wpływu na obliczenia.</summary>
    public string? Payer { get; private set; }

    public string? Comment { get; private set; }

    /// <summary>Klucz idempotencji żądania, które utworzyło wydatek; <c>null</c> dla wydatków bez żądania użytkownika.</summary>
    public Guid? CreationRequestId { get; private set; }

    /// <summary>SHA-256 znormalizowanej komendy utworzenia (32 bajty).</summary>
    public byte[]? CreationRequestHash { get; private set; }

    /// <summary>Definicja cykliczna, której wystąpieniem jest wydatek (SF-16); bez klucza obcego — zostaje po usunięciu definicji.</summary>
    public Guid? RecurringDefinitionId { get; private set; }

    /// <summary>Okres wystąpienia (pierwszy dzień miesiąca); podany wyłącznie razem z <see cref="RecurringDefinitionId"/>.</summary>
    public DateOnly? RecurringPeriod { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public bool IsPaid => PaidOn is not null;

    /// <summary>Wydatek wpisany przez użytkownika, z kluczem idempotencji żądania.</summary>
    /// <exception cref="ArgumentException">Treść narusza reguły, które sprawdza walidacja przed utworzeniem.</exception>
    public static Expense CreateManual(Guid id, ExpenseContent content, Guid creationRequestId, byte[] creationRequestHash, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(creationRequestHash);
        if (id == Guid.Empty || creationRequestId == Guid.Empty)
        {
            throw new ArgumentException("Brak identyfikatora wydatku albo klucza żądania.", nameof(id));
        }

        Require(content);
        return new Expense(id, content, nowUtc)
        {
            CreationRequestId = creationRequestId,
            CreationRequestHash = creationRequestHash,
        };
    }

    /// <summary>
    /// Wystąpienie wydatku cyklicznego (tworzy je wyłącznie definicja): bez daty zapłaty, więc jest
    /// kosztem i planem, a nie wypływem (niezmiennik 1); bez klucza idempotencji — duplikat okresu
    /// odrzuca rejestr okresów.
    /// </summary>
    /// <exception cref="ArgumentException">Treść ma datę zapłaty albo brak identyfikatorów.</exception>
    public static Expense CreateOccurrence(Guid id, ExpenseContent content, Guid recurringDefinitionId, RecurringPeriod period, DateTime nowUtc)
    {
        if (id == Guid.Empty || recurringDefinitionId == Guid.Empty)
        {
            throw new ArgumentException("Brak identyfikatora wydatku albo definicji.", nameof(id));
        }

        Require(content);
        if (content.PaidOn is not null)
        {
            throw new ArgumentException("Wystąpienie wydatku cyklicznego powstaje bez daty zapłaty.", nameof(content));
        }

        return new Expense(id, content, nowUtc)
        {
            RecurringDefinitionId = recurringDefinitionId,
            RecurringPeriod = period.FirstDay,
        };
    }

    /// <summary>Poprawa wszystkich pól (także ustawienie, zmiana i usunięcie daty zapłaty).</summary>
    public ExpenseChangeSet Edit(ExpenseContent content, DateTime nowUtc)
    {
        Require(content);
        var before = Snapshot();
        Apply(content);
        return Changed(before, nowUtc);
    }

    /// <summary>„Oznacz jako opłacony”: data zapłaty i metoda (metoda zastępuje zapisaną, <c>null</c> ją czyści).</summary>
    public ExpenseChangeSet MarkPaid(DateOnly paidOn, ExpensePaymentMethod? method, DateTime nowUtc)
    {
        if (IsPaid)
        {
            return ExpenseChangeSet.Rejected(ExpenseRuleViolation.AlreadyPaid);
        }

        var before = Snapshot();
        PaidOn = paidOn;
        PaymentMethod = method;
        return Changed(before, nowUtc);
    }

    /// <summary>„Cofnij oznaczenie zapłaty”: usuwa datę zapłaty, metoda zostaje.</summary>
    public ExpenseChangeSet UnmarkPaid(DateTime nowUtc)
    {
        if (!IsPaid)
        {
            return ExpenseChangeSet.Rejected(ExpenseRuleViolation.NotPaid);
        }

        var before = Snapshot();
        PaidOn = null;
        return Changed(before, nowUtc);
    }

    /// <summary>Wartości wszystkich pól objętych audytem, w stałej kolejności.</summary>
    public IReadOnlyList<ExpenseFieldValue> Snapshot() =>
    [
        new(ExpenseField.Description, Description),
        new(ExpenseField.Category, Category),
        new(ExpenseField.Counterparty, Counterparty),
        new(ExpenseField.CostDate, CostDate),
        new(ExpenseField.DueDate, DueDate),
        new(ExpenseField.PaidOn, PaidOn),
        new(ExpenseField.GrossAmount, GrossAmount),
        new(ExpenseField.NetAmount, NetAmount),
        new(ExpenseField.DocumentReference, DocumentReference),
        new(ExpenseField.PaymentMethod, PaymentMethod),
        new(ExpenseField.Payer, Payer),
        new(ExpenseField.Comment, Comment),
    ];

    private static void Require(ExpenseContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(content.Description, nameof(content));
        if (ExpenseAmounts.Check(content.GrossAmount, content.NetAmount) != ExpenseAmountError.None)
        {
            throw new ArgumentException("Kwoty naruszają reguły wydatku.", nameof(content));
        }
    }

    private void Apply(ExpenseContent content)
    {
        Description = content.Description;
        Category = content.Category;
        Counterparty = content.Counterparty;
        CostDate = content.CostDate;
        DueDate = content.DueDate;
        PaidOn = content.PaidOn;
        GrossAmount = content.GrossAmount;
        NetAmount = content.NetAmount;
        DocumentReference = content.DocumentReference;
        PaymentMethod = content.PaymentMethod;
        Payer = content.Payer;
        Comment = content.Comment;
    }

    private ExpenseChangeSet Changed(IReadOnlyList<ExpenseFieldValue> before, DateTime nowUtc)
    {
        var after = Snapshot();
        var changes = new List<ExpenseFieldChange>();
        for (var index = 0; index < before.Count; index++)
        {
            if (!Equals(before[index].Value, after[index].Value))
            {
                changes.Add(new ExpenseFieldChange(before[index].Field, before[index].Value, after[index].Value));
            }
        }

        if (changes.Count > 0)
        {
            UpdatedAtUtc = nowUtc;
        }

        return new ExpenseChangeSet(changes);
    }
}
