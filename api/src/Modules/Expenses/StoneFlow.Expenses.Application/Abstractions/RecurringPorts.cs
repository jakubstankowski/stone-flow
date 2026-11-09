using StoneFlow.Expenses.Application.ListRecurringExpenses;
using StoneFlow.Expenses.Domain.Expenses;
using StoneFlow.Expenses.Domain.Recurring;

namespace StoneFlow.Expenses.Application.Abstractions;

/// <summary>
/// Blokada wiersza definicji w transakcji jednostki pracy (wzór <c>IOrderLock</c>): zmiana użytkownika
/// i generowanie tej samej definicji wykonują się po kolei. Bierze ją na początku każdy przypadek użycia,
/// który zmienia definicję albo tworzy jej wystąpienia (poza dodaniem).
/// </summary>
public interface IRecurringDefinitionLock
{
    /// <summary>Blokuje wiersz do końca transakcji; <c>false</c>, gdy definicji nie ma.</summary>
    Task<bool> LockAsync(Guid definitionId, CancellationToken cancellationToken);
}

/// <summary>Definicja wczytana do zmiany (z dniami i wyłączeniami) razem z zapisaną wersją.</summary>
public sealed record StoredRecurringDefinition(RecurringExpenseDefinition Definition, byte[] Version);

/// <summary>Wynik zapisu wystąpień z wierszami rejestru.</summary>
public enum OccurrenceSaveStatus
{
    Saved,

    /// <summary>
    /// Naruszenie klucza rejestru (definicja, okres): okres utworzył ktoś inny. To nie błąd biznesowy —
    /// wywołujący wycofuje jednostkę pracy.
    /// </summary>
    DuplicatePeriod,

    /// <summary>Naruszenie klucza obcego: definicja usunięta w trakcie.</summary>
    DefinitionMissing,
}

/// <summary>
/// Zapis definicji, wystąpień z rejestrem i kluczy idempotencji w transakcji jednostki pracy. Konflikt
/// wersji, kolizja rejestru i duplikat klucza to wyniki, nie wyjątki; zakleszczenie (1205) nie jest
/// tłumaczone.
/// </summary>
public interface IRecurringExpenseStore
{
    /// <summary>Wczytuje definicję do zmiany; <c>null</c>, gdy nie istnieje.</summary>
    Task<StoredRecurringDefinition?> LoadAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Okresy definicji z rejestru i ich terminy (termin efektywny).</summary>
    Task<IReadOnlyDictionary<RecurringPeriod, DateOnly>> RegisteredPeriodsAsync(Guid definitionId, CancellationToken cancellationToken);

    Task<StoreSaveResult> AddAsync(RecurringExpenseDefinition definition, CancellationToken cancellationToken);

    /// <summary>Zapisuje zmiany wczytanej definicji, o ile wersja w bazie równa się <paramref name="expectedVersion"/>.</summary>
    Task<StoreSaveResult> SaveAsync(RecurringExpenseDefinition definition, byte[] expectedVersion, CancellationToken cancellationToken);

    /// <summary>Usuwa definicję fizycznie (kaskadą dni, wyłączenia i rejestr), o ile wersja się zgadza.</summary>
    Task<StoreSaveResult> DeleteAsync(RecurringExpenseDefinition definition, byte[] expectedVersion, CancellationToken cancellationToken);

    /// <summary>Dodaje wsad wystąpień i wierszy rejestru jednym zapisem.</summary>
    Task<OccurrenceSaveStatus> AddOccurrencesAsync(IReadOnlyList<RecurringOccurrence> occurrences, CancellationToken cancellationToken);

    /// <summary>Dodaje klucz idempotencji; naruszenie klucza to <see cref="StoreSaveStatus.DuplicateRequest"/> z wyjątkiem.</summary>
    Task<StoreSaveResult> AddRequestAsync(RecurringDefinitionRequest request, CancellationToken cancellationToken);
}

/// <summary>Zapisany klucz idempotencji dodania definicji.</summary>
public sealed record RecurringRequestMatch(Guid DefinitionId, byte[] RequestHash, int CreatedOccurrences);

/// <summary>Stan harmonogramu definicji i jej rejestr okresów (wejście czystych funkcji domeny).</summary>
public sealed record RecurringScheduleData(RecurringScheduleState State, IReadOnlyDictionary<RecurringPeriod, DateOnly> Registry);

/// <summary>Wiersz listy definicji z bazy (surowe wartości) z harmonogramem do policzenia stanu i terminu.</summary>
public sealed record RecurringListRowDto(
    Guid Id,
    string Description,
    ExpenseCategory Category,
    string? Counterparty,
    decimal GrossAmount,
    int DayOfMonth,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    DateOnly? LastDueDate,
    DateOnly? PausedOn,
    RecurringScheduleData Schedule);

/// <summary>Strona wierszy; <see cref="Total"/> to liczba wszystkich definicji spełniających filtry.</summary>
public sealed record RecurringRowsPage(IReadOnlyList<RecurringListRowDto> Rows, int Total);

/// <summary>Surowe dane definicji (znaczniki w UTC) z liczbą istniejących wystąpień.</summary>
public sealed record RecurringDefinitionRecordDto(
    Guid Id,
    string Description,
    ExpenseCategory Category,
    string? Counterparty,
    decimal GrossAmount,
    decimal? NetAmount,
    ExpensePaymentMethod? PaymentMethod,
    string? Payer,
    string? Comment,
    int DayOfMonth,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    DateOnly? LastDueDate,
    DateOnly? PausedOn,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    byte[] Version,
    int OccurrenceCount,
    RecurringScheduleData Schedule);

/// <summary>Dane podglądu zmiany: harmonogram, rejestr i terminy (z rejestru) istniejących wystąpień.</summary>
public sealed record RecurringPreviewData(RecurringScheduleData Schedule, IReadOnlyList<DateOnly> ExistingOccurrenceDueDates);

/// <summary>Kandydat fazy odczytu zadania.</summary>
public sealed record RecurringGenerationCandidate(Guid Id, RecurringScheduleData Schedule);

/// <summary>Odczyty definicji jako DTO (bez śledzenia zmian), stałą liczbą zapytań na wywołanie.</summary>
public interface IRecurringExpenseReader
{
    Task<RecurringRowsPage> ListAsync(RecurringListCriteria criteria, CancellationToken cancellationToken);

    /// <summary>Surowe dane definicji; <c>null</c>, gdy nie istnieje.</summary>
    Task<RecurringDefinitionRecordDto?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Dane podglądu zmiany; <c>null</c>, gdy definicji nie ma.</summary>
    Task<RecurringPreviewData?> GetPreviewDataAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Kandydaci zadania: niewstrzymane, z początkiem ≤ <paramref name="today"/> i bez końca albo z końcem
    /// nie dawniej niż 400 dni temu — z harmonogramem i rejestrem.
    /// </summary>
    Task<IReadOnlyList<RecurringGenerationCandidate>> ListGenerationCandidatesAsync(DateOnly today, CancellationToken cancellationToken);

    Task<RecurringRequestMatch?> FindRequestAsync(Guid key, CancellationToken cancellationToken);

    /// <summary>Aktualna wersja definicji; <c>null</c>, gdy nie istnieje.</summary>
    Task<byte[]?> GetVersionAsync(Guid id, CancellationToken cancellationToken);
}
