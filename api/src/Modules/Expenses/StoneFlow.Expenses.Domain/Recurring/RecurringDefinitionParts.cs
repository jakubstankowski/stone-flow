using StoneFlow.Expenses.Domain.Expenses;

namespace StoneFlow.Expenses.Domain.Recurring;

/// <summary>Wpis historii dni definicji (część definicji, tabela <c>RecurringExpenseDays</c>).</summary>
public sealed class RecurringExpenseDay
{
    internal RecurringExpenseDay(RecurringPeriod fromPeriod, int dayOfMonth)
    {
        FromPeriod = fromPeriod.FirstDay;
        DayOfMonth = dayOfMonth;
    }

    // Dla EF Core.
    private RecurringExpenseDay()
    {
    }

    /// <summary>Pierwszy dzień okresu, od którego obowiązuje dzień.</summary>
    public DateOnly FromPeriod { get; private set; }

    public int DayOfMonth { get; internal set; }
}

/// <summary>Przedział wyłączenia terminów (część definicji, tabela <c>RecurringExpenseExclusions</c>; wiersze tylko dodawane).</summary>
public sealed class RecurringExpenseExclusion
{
    internal RecurringExpenseExclusion(Guid id, RecurringExclusionRange range, DateTime nowUtc)
    {
        Id = id;
        FromDate = range.FromDate;
        ToDate = range.ToDate;
        Reason = range.Reason;
        CreatedAtUtc = nowUtc;
    }

    // Dla EF Core.
    private RecurringExpenseExclusion()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Początek przedziału (włącznie).</summary>
    public DateOnly FromDate { get; private set; }

    /// <summary>Koniec przedziału (wyłącznie).</summary>
    public DateOnly ToDate { get; private set; }

    public RecurringExclusionReason Reason { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
}

/// <summary>
/// Wiersz rejestru okresów (definicja, okres): powstaje razem z wystąpieniem w tej samej jednostce
/// pracy i nie znika przy usunięciu wydatku, więc okres nie dostaje drugiego wystąpienia (niezmiennik 4).
/// </summary>
public sealed class RecurringExpensePeriod
{
    internal RecurringExpensePeriod(Guid definitionId, RecurringPeriod period, DateOnly dueDate, Guid expenseId, DateTime nowUtc)
    {
        DefinitionId = definitionId;
        Period = period.FirstDay;
        DueDate = dueDate;
        ExpenseId = expenseId;
        CreatedAtUtc = nowUtc;
    }

    // Dla EF Core.
    private RecurringExpensePeriod()
    {
    }

    public Guid DefinitionId { get; private set; }

    /// <summary>Pierwszy dzień okresu.</summary>
    public DateOnly Period { get; private set; }

    /// <summary>Termin w chwili utworzenia wystąpienia (termin efektywny okresu).</summary>
    public DateOnly DueDate { get; private set; }

    public Guid ExpenseId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
}

/// <summary>Utworzone wystąpienie: wydatek i wiersz rejestru, zapisywane razem.</summary>
public sealed record RecurringOccurrence(Expense Expense, RecurringExpensePeriod Period);

/// <summary>
/// Klucz idempotencji dodania definicji (<c>Idempotency-Key</c>). Zostaje po usunięciu definicji, więc
/// powtórzenie żądania nie tworzy minionych kosztów drugi raz.
/// </summary>
public sealed class RecurringDefinitionRequest
{
    private RecurringDefinitionRequest(Guid key, Guid definitionId, byte[] requestHash, int createdOccurrences, DateTime nowUtc)
    {
        Key = key;
        DefinitionId = definitionId;
        RequestHash = requestHash;
        CreatedOccurrences = createdOccurrences;
        CreatedAtUtc = nowUtc;
    }

    // Dla EF Core.
    private RecurringDefinitionRequest()
    {
        RequestHash = [];
    }

    public Guid Key { get; private set; }

    public Guid DefinitionId { get; private set; }

    /// <summary>SHA-256 znormalizowanej treści żądania (32 bajty).</summary>
    public byte[] RequestHash { get; private set; }

    /// <summary>Liczba wystąpień utworzonych przy pierwszym zapisie, zwracana przy powtórzeniu.</summary>
    public int CreatedOccurrences { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public static RecurringDefinitionRequest Create(Guid key, Guid definitionId, byte[] requestHash, int createdOccurrences, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(requestHash);
        ArgumentOutOfRangeException.ThrowIfNegative(createdOccurrences);
        if (key == Guid.Empty || definitionId == Guid.Empty)
        {
            throw new ArgumentException("Brak klucza żądania albo identyfikatora definicji.", nameof(key));
        }

        return new RecurringDefinitionRequest(key, definitionId, requestHash, createdOccurrences, nowUtc);
    }
}
