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
public sealed record LockedOrder(
    string Number,
    decimal Value,
    bool IsCancelled,
    decimal PaymentsTotal,
    decimal RefundsTotal,
    decimal SalesReturnsTotal,
    decimal CorrectionsTotal)
{
    public EntryTotals Totals => new(PaymentsTotal, RefundsTotal)
    {
        Value = Value,
        SalesReturns = SalesReturnsTotal,
        Corrections = CorrectionsTotal,
    };
}

/// <summary>
/// Blokada wiersza zamówienia: jedyna droga serializacji zapisów wpisów zamówienia (wpłaty,
/// zwroty, korekty sprzedaży) i obniżenia jego wartości (limit obniżeń, SF-10). Każdy taki zapis
/// bierze blokadę przed liczeniem sum. Blokada nie zmienia wersji zamówienia.
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

/// <summary>
/// Okres retencji danych zamówień z konfiguracji wdrożenia (SF-22, D-09). <c>null</c> = funkcja
/// anonimizacji wyłączona; brak wartości zastępczej.
/// </summary>
public interface IAnonymizationSettings
{
    /// <summary>Liczba miesięcy kalendarzowych (1–1200) albo <c>null</c>.</summary>
    int? RetentionMonths { get; }
}

/// <summary>
/// Fakty retencji z wpłat, zwrotów i korekt zamówienia: najpóźniejsza data biznesowa wpisu i najpóźniejszy
/// czas zmiany wpisu (UTC); <c>null</c>, gdy zamówienie nie ma wpisów.
/// </summary>
public sealed record EntryRetentionFacts(DateOnly? LastEntryDate, DateTime? LastEntryChangeUtc);

/// <summary>Zwroty i korekty sprzedaży zamówienia wczytane do zmiany (anonimizacja powodu).</summary>
public sealed record ReasonEntries(IReadOnlyList<Refund> Refunds, IReadOnlyList<SalesCorrection> SalesCorrections);

/// <summary>
/// Dane wpisów zamówienia potrzebne do anonimizacji (SF-22). Wpisy do zmiany są śledzone i zapisuje je
/// <see cref="IOrderStore.SaveAsync"/> (ten sam kontekst, jedno <c>SaveChanges</c>).
/// </summary>
public interface IOrderAnonymizationStore
{
    Task<EntryRetentionFacts> GetRetentionFactsAsync(Guid orderId, CancellationToken cancellationToken);

    Task<ReasonEntries> LoadReasonEntriesAsync(Guid orderId, CancellationToken cancellationToken);
}

/// <summary>
/// Zamówienia ze sklepu i znaczniki usunięcia (SF-12), w transakcji jednostki pracy. Blokada po ID
/// zewnętrznym obejmuje też zamówienie, którego jeszcze nie ma, więc równoczesne dostawy tego samego
/// zamówienia się szeregują.
/// </summary>
public interface IShopOrderStore
{
    /// <summary>Blokada wyłączna na ID zamówienia w sklepie do końca transakcji.</summary>
    Task LockAsync(string externalId, CancellationToken cancellationToken);

    /// <summary>Zamówienie ze sklepu do zmiany; <c>null</c>, gdy go nie ma.</summary>
    Task<StoredOrder?> FindAsync(string externalId, CancellationToken cancellationToken);

    /// <summary>Czas odbioru usunięcia zamówienia, którego ERP nie miał; <c>null</c>, gdy brak znacznika.</summary>
    Task<DateTime?> FindDeletionAsync(string externalId, CancellationToken cancellationToken);

    /// <summary>Zapisuje znacznik usunięcia, jeśli go nie ma (ponowienie nie zmienia czasu).</summary>
    Task<StoreSaveStatus> RecordDeletionAsync(string externalId, DateTime receivedAtUtc, CancellationToken cancellationToken);

    Task RemoveDeletionAsync(string externalId, CancellationToken cancellationToken);
}
