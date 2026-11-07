using StoneFlow.Expenses.Application.GetExpense;
using StoneFlow.Expenses.Application.ListExpenses;
using StoneFlow.Expenses.Domain.Expenses;

namespace StoneFlow.Expenses.Application.Abstractions;

/// <summary>Wydatek wczytany do zmiany razem z zapisaną wersją (token współbieżności).</summary>
public sealed record StoredExpense(Expense Expense, byte[] Version);

/// <summary>Wynik zapisu przez port zapisu wydatków.</summary>
public enum StoreSaveStatus
{
    /// <summary>Zapisano (także usunięto).</summary>
    Saved,

    /// <summary>Wersja w bazie różni się od oczekiwanej: ktoś zmienił albo usunął wydatek w międzyczasie.</summary>
    Conflict,

    /// <summary>
    /// Naruszenie unikalności przy dodaniu. Rozróżnienie (klucz idempotencji albo błąd programu)
    /// należy do wywołującego, po wycofaniu jednostki pracy.
    /// </summary>
    DuplicateRequest,
}

/// <summary>
/// Wynik zapisu: nowa wersja po <see cref="StoreSaveStatus.Saved"/> (poza usunięciem); oryginalny
/// wyjątek bazy po <see cref="StoreSaveStatus.DuplicateRequest"/>, aby wywołujący mógł go zgłosić.
/// </summary>
public sealed record StoreSaveResult(StoreSaveStatus Status, byte[]? Version = null, Exception? Error = null);

/// <summary>Zapis wydatków w transakcji jednostki pracy. Konflikt wersji i duplikat to wyniki, nie wyjątki.</summary>
public interface IExpenseStore
{
    /// <summary>Wczytuje wydatek do zmiany; <c>null</c>, gdy nie istnieje.</summary>
    Task<StoredExpense?> LoadAsync(Guid id, CancellationToken cancellationToken);

    Task<StoreSaveResult> AddAsync(Expense expense, CancellationToken cancellationToken);

    /// <summary>Zapisuje zmiany wczytanego wydatku, o ile wersja w bazie równa się <paramref name="expectedVersion"/>.</summary>
    Task<StoreSaveResult> SaveAsync(Expense expense, byte[] expectedVersion, CancellationToken cancellationToken);

    /// <summary>Usuwa wydatek fizycznie, o ile wersja w bazie równa się <paramref name="expectedVersion"/>.</summary>
    Task<StoreSaveResult> DeleteAsync(Expense expense, byte[] expectedVersion, CancellationToken cancellationToken);
}

/// <summary>Wydatek utworzony żądaniem o danym kluczu idempotencji.</summary>
public sealed record ExpenseCreationMatch(Guid Id, byte[] RequestHash, byte[] Version);

/// <summary>Odczyty wydatków jako DTO (bez śledzenia zmian).</summary>
public interface IExpenseReader
{
    Task<ExpenseRowsPage> ListAsync(ExpenseListCriteria criteria, CancellationToken cancellationToken);

    /// <summary>Surowe dane wydatku; <c>null</c>, gdy nie istnieje.</summary>
    Task<ExpenseRecordDto?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<ExpenseCreationMatch?> FindByCreationRequestAsync(Guid creationRequestId, CancellationToken cancellationToken);
}
