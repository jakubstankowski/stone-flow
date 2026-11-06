namespace StoneFlow.Integrations.Application.WooCommerce;

/// <summary>Zestaw zamówień pobierany przez uzupełnianie.</summary>
public enum BackfillSet
{
    /// <summary><c>status=any</c> z oknem dat modyfikacji (bez kosza).</summary>
    Any,

    /// <summary><c>status=trash</c> bez filtrów dat (kosz jest mały, zastosowanie idempotentne).</summary>
    Trash,
}

/// <summary>Żądanie jednej strony listy zamówień REST v3.</summary>
/// <param name="Set">Zestaw zamówień.</param>
/// <param name="ModifiedAfterUtc">Dolna granica daty modyfikacji (tylko <see cref="BackfillSet.Any"/>).</param>
/// <param name="ModifiedBeforeUtc">Górna granica daty modyfikacji (tylko <see cref="BackfillSet.Any"/>).</param>
/// <param name="Page">Numer strony od 1.</param>
public sealed record RestPageRequest(BackfillSet Set, DateTime? ModifiedAfterUtc, DateTime? ModifiedBeforeUtc, int Page);

/// <summary>Wynik pobrania strony listy zamówień.</summary>
public abstract record RestPageResult
{
    private RestPageResult()
    {
    }

    /// <summary>Strona pobrana: surowe treści zamówień (JSON) i nagłówki paginacji, jeśli były.</summary>
    /// <param name="Orders">Treść każdego zamówienia ze strony.</param>
    /// <param name="Total">Nagłówek <c>X-WP-Total</c>.</param>
    /// <param name="TotalPages">Nagłówek <c>X-WP-TotalPages</c>.</param>
    public sealed record Loaded(IReadOnlyList<byte[]> Orders, int? Total, int? TotalPages) : RestPageResult;

    /// <summary>Strona niepobrana; komunikat złożony przez kod (bez treści odpowiedzi i sekretów).</summary>
    /// <param name="Message">Opis błędu.</param>
    public sealed record Failed(string Message) : RestPageResult;
}

/// <summary>Wynik pobrania jednego zamówienia.</summary>
public abstract record RestOrderResult
{
    private RestOrderResult()
    {
    }

    /// <summary>Zamówienie istnieje; surowa treść (JSON).</summary>
    /// <param name="Body">Treść odpowiedzi.</param>
    public sealed record Found(byte[] Body) : RestOrderResult;

    /// <summary>
    /// <c>404</c> z kodem WooCommerce nieistniejącego zamówienia — zamówienia nie ma w sklepie.
    /// </summary>
    public sealed record NotFound : RestOrderResult;

    /// <summary>Każda inna odpowiedź albo brak odpowiedzi; komunikat złożony przez kod.</summary>
    /// <param name="Message">Opis błędu.</param>
    public sealed record Failed(string Message) : RestOrderResult;
}

/// <summary>Klient REST v3 sklepu (tylko odczyt; klucze w nagłówku <c>Authorization</c>).</summary>
public interface IWooCommerceRestClient
{
    Task<RestPageResult> GetOrdersPageAsync(RestPageRequest request, CancellationToken cancellationToken);

    Task<RestOrderResult> GetOrderAsync(string externalId, CancellationToken cancellationToken);
}

/// <summary>Czy skonfigurowano klucze REST; bez nich uzupełnianie i „Ponów” są wyłączone.</summary>
public interface IWooCommerceRestSettings
{
    bool IsConfigured { get; }
}

/// <summary>Nieudana próba zapisu zamówienia do historii błędów.</summary>
/// <param name="ExternalId">ID zamówienia w sklepie.</param>
/// <param name="Source">Źródło (<c>backfill</c>, <c>webhook</c>, <c>retry</c>).</param>
/// <param name="Message">Komunikat złożony przez kod, bez danych klienta i sekretów.</param>
/// <param name="Context">Temat i ID dostawy, Id przebiegu albo identyfikator korelacji.</param>
/// <param name="ModifiedAtUtc">Data modyfikacji nieudanej dostawy, jeśli czytelna.</param>
public sealed record OrderFailure(string ExternalId, string Source, string Message, string Context, DateTime? ModifiedAtUtc);

/// <summary>Błąd zamówienia z historii (czasy UTC).</summary>
public sealed record OrderErrorView(
    Guid Id,
    string ExternalId,
    string Status,
    string Source,
    DateTime FirstOccurredAtUtc,
    DateTime LastOccurredAtUtc,
    int Attempts,
    string Message,
    string Context,
    DateTime? ClosedAtUtc,
    string? ClosedBy);

/// <summary>Strona historii błędów.</summary>
public sealed record OrderErrorPage(IReadOnlyList<OrderErrorView> Items, int Total);

/// <summary>
/// Historia błędów zamówień: najwyżej jeden otwarty błąd na zamówienie (upsert), zamykanie
/// z warunkiem wersji (<c>OrderErrorRules.CanClose</c>). Zapisy w transakcji własnego kontekstu.
/// </summary>
public interface IWooCommerceOrderErrors
{
    /// <summary>Wstawia otwarty błąd albo aktualizuje istniejący (próby, czas, komunikat, kontekst).</summary>
    Task RecordAsync(OrderFailure failure, CancellationToken cancellationToken);

    /// <summary>Zamyka otwarty błąd zamówienia, jeśli udana dostawa nie jest starsza od nieudanej.</summary>
    /// <returns><c>true</c>, gdy błąd zamknięto.</returns>
    Task<bool> CloseAsync(string externalId, DateTime? modifiedAtUtc, bool isDeletion, string closedBy, CancellationToken cancellationToken);

    Task<OrderErrorView?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Strona błędów malejąco po ostatnim wystąpieniu; <paramref name="status"/> <c>null</c> = wszystkie.</summary>
    Task<OrderErrorPage> ListAsync(string? status, int page, int pageSize, CancellationToken cancellationToken);

    Task<int> CountOpenAsync(CancellationToken cancellationToken);
}

/// <summary>Liczniki przebiegu uzupełniania.</summary>
/// <param name="OrdersProcessed">Zamówienia (różne ID) przekazane do zapisu.</param>
/// <param name="Errors">Zamówienia (różne ID) z błędem zapisu.</param>
/// <param name="Unidentified">Pozycje bez czytelnego ID.</param>
public sealed record SyncRunCounts(int OrdersProcessed, int Errors, int Unidentified);

/// <summary>Przebieg uzupełniania z historii (czasy UTC); <c>Status</c>: <c>running</c>, <c>succeeded</c>, <c>failed</c>.</summary>
public sealed record SyncRunView(
    Guid Id,
    string Source,
    string Status,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    int OrdersProcessed,
    int Errors,
    int Unidentified,
    string? Message);

/// <summary>Kursor sukcesu i przebiegi uzupełniania (schemat <c>integrations</c>).</summary>
public interface IWooCommerceSyncStore
{
    Task<DateTime?> GetCursorAsync(CancellationToken cancellationToken);

    /// <summary>
    /// W jednej transakcji oznacza wszystkie przebiegi <c>running</c> jako przerwane (pod dzierżawą
    /// zadania żaden inny nie trwa) i wstawia nowy przebieg <c>running</c>.
    /// </summary>
    Task StartRunAsync(Guid runId, string source, DateTime startedAtUtc, CancellationToken cancellationToken);

    /// <summary>
    /// W jednej transakcji: przebieg <c>succeeded</c> z licznikami i kursor (gdy podany) — tylko gdy
    /// przebieg jest nadal <c>running</c>.
    /// </summary>
    /// <returns><c>false</c> (nic nie zapisano), gdy przebieg nie jest już <c>running</c>.</returns>
    Task<bool> SucceedRunAsync(Guid runId, DateTime? cursorUtc, DateTime finishedAtUtc, SyncRunCounts counts, CancellationToken cancellationToken);

    /// <summary>Przebieg <c>failed</c> z kontrolowanym komunikatem; kursor bez zmian.</summary>
    Task FailRunAsync(Guid runId, DateTime finishedAtUtc, SyncRunCounts counts, string message, CancellationToken cancellationToken);

    Task<SyncRunView?> GetLastRunAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Jedno miejsce oczekiwania „Synchronizuj teraz”: flaga ustawiona od przyjęcia żądania do końca
/// uruchomienia zadania (implementacja: usługa w tle modułu).
/// </summary>
public interface IManualSyncQueue
{
    /// <summary>Żądanie czeka albo jego przebieg trwa.</summary>
    bool IsPending { get; }

    /// <summary>Przyjmuje żądanie, jeśli żadne nie czeka ani nie trwa.</summary>
    bool TryRequest();
}

/// <summary>Zdarzenia uzupełniania i historii błędów w logach: bez treści, sekretów i danych klienta.</summary>
public interface IWooCommerceSyncEvents
{
    void NotConfigured();

    void RunStarted(Guid runId, string source, DateTime fromUtc, DateTime toUtc);

    void RunSucceeded(Guid runId, SyncRunCounts counts);

    void RunFailed(Guid runId, string message);

    void Unidentified(string source, string context, string problem);

    void ApplyFailed(Exception? exception, string source, string externalId);

    void ErrorRecordFailed(Exception exception, string externalId);
}
