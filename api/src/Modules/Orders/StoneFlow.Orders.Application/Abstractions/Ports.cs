using StoneFlow.Orders.Application.GetOrder;
using StoneFlow.Orders.Application.ListOrders;
using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Orders.Domain.Payments;

namespace StoneFlow.Orders.Application.Abstractions;

/// <summary>Zamówienie wczytane do zmiany razem z zapisaną wersją (token współbieżności).</summary>
public sealed record StoredOrder(Order Order, byte[] Version);

/// <summary>Wynik zapisu przez port zapisu zamówień albo wpisów (wpłat, zwrotów).</summary>
public enum StoreSaveStatus
{
    /// <summary>Zapisano (także usunięto wpis).</summary>
    Saved,

    /// <summary>Wersja w bazie różni się od oczekiwanej: ktoś zmienił zamówienie albo wpis w międzyczasie.</summary>
    Conflict,

    /// <summary>
    /// Naruszenie unikalności przy dodaniu. Rozróżnienie (klucz idempotencji albo błąd programu)
    /// należy do wywołującego, po wycofaniu jednostki pracy.
    /// </summary>
    DuplicateRequest,
}

/// <summary>
/// Wynik zapisu zamówienia albo wpisu (wpłaty, zwrotu): nowa wersja po <see cref="StoreSaveStatus.Saved"/>; oryginalny wyjątek bazy po
/// <see cref="StoreSaveStatus.DuplicateRequest"/>, aby wywołujący mógł go ponownie zgłosić.
/// </summary>
public sealed record StoreSaveResult(StoreSaveStatus Status, byte[]? Version = null, Exception? Error = null);

/// <summary>Zapis zamówień w transakcji jednostki pracy.</summary>
public interface IOrderStore
{
    /// <summary>Wczytuje zamówienie do zmiany; <c>null</c>, gdy nie istnieje.</summary>
    Task<StoredOrder?> LoadAsync(Guid id, CancellationToken cancellationToken);

    Task<StoreSaveResult> AddAsync(Order order, CancellationToken cancellationToken);

    /// <summary>Zapisuje zmiany wczytanego zamówienia, o ile wersja w bazie równa się <paramref name="expectedVersion"/>.</summary>
    Task<StoreSaveResult> SaveAsync(Order order, byte[] expectedVersion, CancellationToken cancellationToken);
}

/// <summary>Kolejny numer zamówienia w roku, w transakcji jednostki pracy (wycofanie cofa licznik).</summary>
public interface IOrderNumberAllocator
{
    Task<int> NextAsync(int year, CancellationToken cancellationToken);
}

/// <summary>Zamówienie utworzone żądaniem o danym kluczu idempotencji.</summary>
public sealed record CreationRequestMatch(Guid Id, string Number, byte[] RequestHash, byte[] Version);

/// <summary>Odczyty zamówień jako DTO (bez śledzenia zmian).</summary>
public interface IOrderReader
{
    Task<OrderListPageDto> ListAsync(OrderListCriteria criteria, CancellationToken cancellationToken);

    Task<OrderDetailsDto?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<CreationRequestMatch?> FindByCreationRequestAsync(Guid creationRequestId, CancellationToken cancellationToken);
}

/// <summary>
/// Zamówienie zablokowane do końca jednostki pracy (<c>UPDLOCK</c>) razem z sumami wpisów
/// policzonymi już po uzyskaniu blokady. Numer służy do etykiety audytu wpisu.
/// </summary>
public sealed record LockedOrder(string Number, decimal Value, bool IsCancelled, decimal PaymentsTotal, decimal RefundsTotal)
{
    public EntryTotals Totals => new(PaymentsTotal, RefundsTotal);
}

/// <summary>
/// Blokada wiersza zamówienia: jedyna droga serializacji zapisów wpisów pieniężnych zamówienia
/// (wpłaty, zwroty, a w SF-10 korekty sprzedaży). Każdy taki zapis zaczyna jednostkę pracy od
/// tego wywołania, przed liczeniem sum. Blokada nie zmienia wersji zamówienia.
/// </summary>
public interface IOrderLock
{
    /// <summary>Blokuje zamówienie w transakcji jednostki pracy; <c>null</c>, gdy nie istnieje.</summary>
    Task<LockedOrder?> LockAsync(Guid orderId, CancellationToken cancellationToken);
}

/// <summary>Wpis wczytany do zmiany razem z zapisaną wersją (token współbieżności).</summary>
public sealed record StoredEntry<TEntry>(TEntry Entry, byte[] Version)
    where TEntry : OrderEntry;

/// <summary>Wpis utworzony żądaniem o danym kluczu idempotencji (w dowolnym zamówieniu).</summary>
public sealed record EntryCreationMatch(Guid Id, Guid OrderId, byte[] RequestHash, byte[] Version);

/// <summary>
/// Zapis wpisów jednego rodzaju (<see cref="Payment"/> albo <see cref="Refund"/>) w transakcji
/// jednostki pracy. Konflikt wersji i naruszenie unikalności są wynikami, nie wyjątkami.
/// </summary>
public interface IOrderEntryStore<TEntry>
    where TEntry : OrderEntry
{
    /// <summary>Wczytuje wpis do zmiany; <c>null</c>, gdy nie istnieje. Przynależność do zamówienia sprawdza wywołujący.</summary>
    Task<StoredEntry<TEntry>?> LoadAsync(Guid id, CancellationToken cancellationToken);

    Task<StoreSaveResult> AddAsync(TEntry entry, CancellationToken cancellationToken);

    /// <summary>Zapisuje zmiany wczytanego wpisu, o ile wersja w bazie równa się <paramref name="expectedVersion"/>.</summary>
    Task<StoreSaveResult> SaveAsync(TEntry entry, byte[] expectedVersion, CancellationToken cancellationToken);

    /// <summary>Usuwa wpis fizycznie, o ile wersja w bazie równa się <paramref name="expectedVersion"/>.</summary>
    Task<StoreSaveResult> DeleteAsync(TEntry entry, byte[] expectedVersion, CancellationToken cancellationToken);

    Task<EntryCreationMatch?> FindByCreationRequestAsync(Guid creationRequestId, CancellationToken cancellationToken);
}
