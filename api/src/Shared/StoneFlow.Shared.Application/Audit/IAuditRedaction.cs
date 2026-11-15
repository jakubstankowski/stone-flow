using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Shared.Application.Audit;

/// <summary>
/// Anonimizacja wybranych pól we wpisach obiektu i jego obiektów podrzędnych (DANE-01, SF-22) —
/// jedyny wyjątek od zasady „wpisy są tylko dodawane” (<see cref="IAuditLog"/>). Obejmuje wpisy,
/// których obiekt to <see cref="AuditRedaction.Root"/> albo których obiekt nadrzędny to
/// <see cref="AuditRedaction.Root"/>, wszystkich operacji i autorów. W pasujących polach wartość
/// stara i nowa różna od <c>null</c> zostaje zastąpiona znacznikiem, <c>null</c> zostaje <c>null</c>;
/// reszta wpisu się nie zmienia.
/// Wywołanie jest dozwolone wyłącznie wewnątrz
/// <see cref="Persistence.IUnitOfWork.ExecuteInTransactionAsync{T}"/>; poza nią kończy się
/// <see cref="InvalidOperationException"/>. Pusta lista selektorów albo pusty znacznik kończą się
/// <see cref="ArgumentException"/>.
/// </summary>
public interface IAuditRedaction
{
    /// <summary>Zwraca liczbę zmienionych wpisów.</summary>
    Task<int> RedactAsync(AuditRedaction redaction, CancellationToken cancellationToken);
}

/// <summary>Zakres anonimizacji: obiekt korzenia, pola do zastąpienia i znacznik.</summary>
public sealed record AuditRedaction(
    AuditObjectReference Root,
    IReadOnlyCollection<AuditFieldSelector> Fields,
    string Marker);

/// <summary>Pole wpisu audytu danego rodzaju obiektu (np. <c>order</c> / <c>customerName</c>).</summary>
public sealed record AuditFieldSelector(AuditObjectType ObjectType, string Field);

/// <summary>
/// Odczyt czasu ostatniego wpisu audytu obiektu i jego obiektów podrzędnych. Używany do początku
/// okresu retencji (SF-22): obejmuje usunięcia wpisów, których wierszy już nie ma. Odczyt jest
/// dozwolony w transakcji jednostki pracy i poza nią.
/// </summary>
public interface IAuditActivity
{
    /// <summary>
    /// Najpóźniejszy czas wpisu (UTC) obiektu <paramref name="root"/> i obiektów, których rodzicem jest
    /// <paramref name="root"/>; <c>null</c>, gdy wpisów nie ma.
    /// </summary>
    Task<DateTime?> LastRecordedAtUtcAsync(AuditObjectReference root, CancellationToken cancellationToken);
}
