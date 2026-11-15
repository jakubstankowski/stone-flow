using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Shared.Application.Audit;

/// <summary>
/// Zapis audytu zmian finansowych (AUD-01). Wpis trafia do tej samej transakcji co zmiana danych,
/// dlatego wywołanie jest dozwolone wyłącznie wewnątrz
/// <see cref="Persistence.IUnitOfWork.ExecuteInTransactionAsync{T}"/>; poza nią kończy się
/// <see cref="InvalidOperationException"/>. Błąd zapisu wycofuje całą transakcję.
/// Czas wpisu (UTC) ustala zapis audytu. Wpisy są tylko dodawane; jedyna dozwolona zmiana zapisanego
/// wpisu to zastąpienie wartości wskazanych pól znacznikiem przez <see cref="IAuditRedaction"/>
/// (anonimizacja danych klienta, DANE-01, SF-22).
/// </summary>
public interface IAuditLog
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken);
}

/// <summary>
/// Wpis audytu. <paramref name="Changes"/>: przy utworzeniu nowe wartości, przy zmianie tylko
/// zmienione pola, przy usunięciu ostatnie wartości obiektu (ślad usunięcia); lista nie może być pusta.
/// </summary>
public sealed record AuditEntry(
    AuditAuthor Author,
    AuditObject Object,
    AuditOperation Operation,
    IReadOnlyList<AuditChange> Changes);

/// <summary>Autor zmiany: konto (identyfikator z komendy) albo autor systemowy.</summary>
public sealed record AuditAuthor
{
    private AuditAuthor(AuditAuthorKind kind, Guid? accountId)
    {
        Kind = kind;
        AccountId = accountId;
    }

    public static AuditAuthor WooCommerceSync { get; } = new(AuditAuthorKind.WooCommerceSync, null);

    public static AuditAuthor AutomatedJob { get; } = new(AuditAuthorKind.AutomatedJob, null);

    public AuditAuthorKind Kind { get; }

    /// <summary>Identyfikator konta; tylko dla <see cref="AuditAuthorKind.Account"/>.</summary>
    public Guid? AccountId { get; }

    public static AuditAuthor Account(Guid accountId) => new(AuditAuthorKind.Account, accountId);
}

/// <summary>
/// Obiekt zmiany. <paramref name="Label"/> to etykieta z chwili zmiany (np. numer zamówienia),
/// <b>bez danych osobowych</b>, dzięki której usunięty obiekt pozostaje rozpoznawalny.
/// <paramref name="Parent"/>: zamówienie dla wpłaty, zwrotu i korekty sprzedaży (wymagane),
/// definicja cykliczna dla wystąpienia wydatku cyklicznego; dla pozostałych brak.
/// </summary>
public sealed record AuditObject(
    AuditObjectType Type,
    Guid Id,
    DateOnly BusinessDate,
    string Label,
    AuditObjectReference? Parent = null);

/// <summary>Odniesienie do obiektu nadrzędnego.</summary>
public sealed record AuditObjectReference(AuditObjectType Type, Guid Id);

/// <summary>
/// Zmiana pola. Wartości są tekstem przygotowanym przez wywołującego (kwoty dziesiętnie z kropką,
/// kultura niezmienna). <paramref name="IsPersonalData"/> oznacza dane osobowe do anonimizacji.
/// </summary>
public sealed record AuditChange(string Field, string? OldValue, string? NewValue, bool IsPersonalData = false);
