using StoneFlow.Orders.Application.GetOrder;
using StoneFlow.Orders.Application.ListOrders;
using StoneFlow.Orders.Domain.Orders;

namespace StoneFlow.Orders.Application.Abstractions;

/// <summary>Zamówienie wczytane do zmiany razem z zapisaną wersją (token współbieżności).</summary>
public sealed record StoredOrder(Order Order, byte[] Version);

public enum OrderSaveStatus
{
    Saved,

    /// <summary>Wersja w bazie różni się od oczekiwanej: ktoś zmienił zamówienie w międzyczasie.</summary>
    Conflict,

    /// <summary>
    /// Naruszenie unikalności przy dodaniu. Rozróżnienie (klucz idempotencji albo błąd programu)
    /// należy do wywołującego, po wycofaniu jednostki pracy.
    /// </summary>
    DuplicateRequest,
}

/// <summary>
/// Wynik zapisu: nowa wersja po <see cref="OrderSaveStatus.Saved"/>; oryginalny wyjątek bazy po
/// <see cref="OrderSaveStatus.DuplicateRequest"/>, aby wywołujący mógł go ponownie zgłosić.
/// </summary>
public sealed record OrderSaveResult(OrderSaveStatus Status, byte[]? Version = null, Exception? Error = null);

/// <summary>Zapis zamówień w transakcji jednostki pracy.</summary>
public interface IOrderStore
{
    /// <summary>Wczytuje zamówienie do zmiany; <c>null</c>, gdy nie istnieje.</summary>
    Task<StoredOrder?> LoadAsync(Guid id, CancellationToken cancellationToken);

    Task<OrderSaveResult> AddAsync(Order order, CancellationToken cancellationToken);

    /// <summary>Zapisuje zmiany wczytanego zamówienia, o ile wersja w bazie równa się <paramref name="expectedVersion"/>.</summary>
    Task<OrderSaveResult> SaveAsync(Order order, byte[] expectedVersion, CancellationToken cancellationToken);
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
