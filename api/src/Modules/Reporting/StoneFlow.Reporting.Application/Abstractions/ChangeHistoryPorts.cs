using StoneFlow.Reporting.Domain.Audit;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Reporting.Application.Abstractions;

/// <summary>
/// Kryteria listy „Historia zmian” (SF-18): przedział UTC <c>[FromUtc, ToUtcExclusive)</c> czasu
/// zmiany, opcjonalne granice daty biznesowej (włącznie, każda osobno) i rodzaje obiektu (pusty
/// zbiór = wszystkie). Sortowanie po czasie, a przy remisie po identyfikatorze wpisu, w tym samym kierunku.
/// </summary>
public sealed record ChangeHistoryCriteria(
    DateTime FromUtc,
    DateTime ToUtcExclusive,
    DateOnly? BusinessFrom,
    DateOnly? BusinessTo,
    IReadOnlySet<AuditObjectType> ObjectTypes,
    bool Descending,
    int Page,
    int PageSize);

/// <summary>
/// Wpis historii zmian. <see cref="OccurredAtUtc"/> ma <see cref="DateTimeKind.Utc"/>.
/// <see cref="Deleted"/> mówi, czy w audycie jest wpis usunięcia tego obiektu (niezależnie od
/// filtrów). Identyfikator konta autora nie wychodzi poza port (jedno konto, D-05).
/// </summary>
public sealed record ChangeHistoryEntryDto(
    long Id,
    DateTime OccurredAtUtc,
    AuditAuthorKind AuthorKind,
    AuditObjectType ObjectType,
    Guid ObjectId,
    DateOnly BusinessDate,
    string Label,
    AuditObjectType? ParentType,
    Guid? ParentId,
    bool Deleted,
    AuditOperation Operation,
    IReadOnlyList<AuditFieldChange> Changes);

/// <summary>Strona listy; <see cref="Total"/> to liczba wpisów całego filtra.</summary>
public sealed record ChangeHistoryPageDto(IReadOnlyList<ChangeHistoryEntryDto> Items, int Total, int Page, int PageSize);

/// <summary>Port odczytu wpisów audytu do listy „Historia zmian”; filtry, sortowanie i strona w bazie.</summary>
public interface IChangeHistoryReader
{
    /// <summary>
    /// Strona wpisów według kryteriów. Strona za końcem daje puste <c>Items</c> z prawdziwym
    /// <c>Total</c>; <c>Deleted</c> nie zależy od filtrów.
    /// </summary>
    Task<ChangeHistoryPageDto> ListAsync(ChangeHistoryCriteria criteria, CancellationToken cancellationToken);
}
