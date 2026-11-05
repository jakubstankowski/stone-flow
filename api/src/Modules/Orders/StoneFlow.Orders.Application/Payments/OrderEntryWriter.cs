using System.Runtime.ExceptionServices;
using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Application.Persistence;

namespace StoneFlow.Orders.Application.Payments;

public enum EntryWriteOutcome
{
    /// <summary>Dodano wpis.</summary>
    Created,

    /// <summary>Powtórzenie dodania z tym samym kluczem i tymi samymi danymi (to samo zamówienie): istniejący wpis.</summary>
    Repeated,

    /// <summary>Ten sam klucz z innymi danymi albo przy innym zamówieniu: nowy wpis nie powstaje.</summary>
    KeyReused,

    /// <summary>Zapisano poprawkę albo nic się nie zmieniło (wersja bez zmian).</summary>
    Saved,

    Deleted,

    ValidationFailed,
    OrderNotFound,

    /// <summary>Wpis nie istnieje albo należy do innego zamówienia.</summary>
    EntryNotFound,

    Conflict,
    PaidWouldBeNegative,
    RefundExceedsPaid,
}

/// <summary>
/// Wynik zapisu wpisu. <see cref="Id"/> dla dodania i <see cref="EntryWriteOutcome.KeyReused"/>;
/// <see cref="Version"/> dla dodania, powtórzenia i poprawy; <see cref="Available"/> dla
/// <see cref="EntryWriteOutcome.RefundExceedsPaid"/>.
/// </summary>
public sealed record EntryWriteResult(
    EntryWriteOutcome Outcome,
    Guid? Id = null,
    byte[]? Version = null,
    FieldErrors? Errors = null,
    decimal? Available = null)
{
    public static EntryWriteResult Invalid(FieldErrors errors) => new(EntryWriteOutcome.ValidationFailed, Errors: errors);
}

/// <summary>
/// Wspólny przebieg dodania, poprawy i usunięcia wpisu (właściciele: handlery <c>AddPayment</c>,
/// <c>EditPayment</c>, <c>DeletePayment</c> i odpowiedniki zwrotów; walidację wejścia robi handler).
/// Każdy zapis to jedna jednostka pracy, która zaczyna się od blokady zamówienia
/// (<see cref="IOrderLock"/>), a sumy są liczone dopiero po jej uzyskaniu. Zatwierdzany jest
/// wyłącznie sukces; reguła, konflikt czy brak obiektu wycofują jednostkę pracy.
/// </summary>
public sealed class OrderEntryWriter<TEntry>(
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    IOrderLock orderLock,
    IOrderEntryStore<TEntry> store,
    TimeProvider timeProvider)
    where TEntry : OrderEntry
{
    /// <summary>
    /// Kolejność: blokada (404), klucz idempotencji (powtórzenie albo konflikt klucza, także przy
    /// innym zamówieniu), reguła limitu (422), zapis, audyt <c>Created</c>. Klucz jest sprawdzany
    /// przed limitem, więc powtórzenie zwrotu, który wyczerpał Zapłacono, daje powtórzenie, a nie
    /// 422. Wyścig o ten sam klucz w różnych zamówieniach kończy się naruszeniem unikalnego
    /// indeksu: po wycofaniu wpis jest czytany po kluczu na nowo. <c>check</c> to reguła limitu dla
    /// sum pod blokadą (<c>null</c>, gdy dodanie zawsze ją spełnia), <c>create</c> tworzy wpis dla
    /// chwili zapisu (UTC).
    /// </summary>
    public async Task<EntryWriteResult> AddAsync(
        Guid accountId,
        Guid orderId,
        Guid idempotencyKey,
        byte[] requestHash,
        Func<EntryTotals, PaidRuleViolation?>? check,
        Func<DateTime, TEntry> create,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestHash);
        ArgumentNullException.ThrowIfNull(create);

        var attempt = await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var order = await orderLock.LockAsync(orderId, ct);
                if (order is null)
                {
                    return new AddAttempt(new EntryWriteResult(EntryWriteOutcome.OrderNotFound), null);
                }

                if (await store.FindByCreationRequestAsync(idempotencyKey, ct) is { } existing)
                {
                    return new AddAttempt(Resolve(existing, requestHash), null);
                }

                if (check?.Invoke(order.Totals) is { } violation)
                {
                    return new AddAttempt(Rejected(violation), null);
                }

                var entry = create(timeProvider.GetUtcNow().UtcDateTime);
                var saved = await store.AddAsync(entry, ct);
                if (saved.Status != StoreSaveStatus.Saved)
                {
                    return new AddAttempt(null, saved.Error ?? new InvalidOperationException("Nie udało się zapisać wpisu."));
                }

                await auditLog.RecordAsync(PaymentAudit.Created(entry, order.Number, accountId), ct);
                return new AddAttempt(new EntryWriteResult(EntryWriteOutcome.Created, entry.Id, saved.Version), null);
            },
            commitWhen: attempt => attempt.Result?.Outcome == EntryWriteOutcome.Created,
            cancellationToken);

        if (attempt.Result is { } result)
        {
            return result;
        }

        var racing = await store.FindByCreationRequestAsync(idempotencyKey, cancellationToken);
        if (racing is null)
        {
            // Naruszenie unikalności innego indeksu niż klucz idempotencji: błąd programu.
            ExceptionDispatchInfo.Throw(attempt.Duplicate!);
        }

        return Resolve(racing, requestHash);
    }

    /// <summary>
    /// Kolejność: blokada (404 zamówienia), wpis tego zamówienia (404 wpisu), wersja (409), reguła
    /// dla sum pod blokadą (422), poprawa, brak zmian (sukces bez zapisu), zapis z kontrolą wersji
    /// i audyt <c>Updated</c>. <c>check</c> dostaje wpis przed poprawą i sumy pod blokadą; <c>edit</c>
    /// poprawia wpis w chwili zapisu (UTC) i zwraca zmienione pola.
    /// </summary>
    public Task<EntryWriteResult> EditAsync(
        Guid accountId,
        Guid orderId,
        Guid entryId,
        byte[] version,
        Func<TEntry, EntryTotals, PaidRuleViolation?> check,
        Func<TEntry, DateTime, IReadOnlyList<EntryFieldChange>> edit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(edit);

        return ChangeAsync(
            orderId,
            entryId,
            version,
            check,
            async (order, stored, ct) =>
            {
                var changes = edit(stored.Entry, timeProvider.GetUtcNow().UtcDateTime);
                if (changes.Count == 0)
                {
                    return new EntryWriteResult(EntryWriteOutcome.Saved, stored.Entry.Id, stored.Version);
                }

                var saved = await store.SaveAsync(stored.Entry, version, ct);
                if (saved.Status == StoreSaveStatus.Conflict)
                {
                    return new EntryWriteResult(EntryWriteOutcome.Conflict);
                }

                await auditLog.RecordAsync(PaymentAudit.Updated(stored.Entry, changes, order.Number, accountId), ct);
                return new EntryWriteResult(EntryWriteOutcome.Saved, stored.Entry.Id, saved.Version);
            },
            cancellationToken);
    }

    /// <summary>
    /// Kolejność jak w poprawie; usunięcie fizyczne z kontrolą wersji i audyt <c>Deleted</c> ze
    /// wszystkimi polami wpisu. <c>check</c> to reguła limitu (<c>null</c>, gdy usunięcie zawsze ją
    /// spełnia, jak przy zwrocie).
    /// </summary>
    public Task<EntryWriteResult> DeleteAsync(
        Guid accountId,
        Guid orderId,
        Guid entryId,
        byte[] version,
        Func<TEntry, EntryTotals, PaidRuleViolation?>? check,
        CancellationToken cancellationToken) =>
        ChangeAsync(
            orderId,
            entryId,
            version,
            check,
            async (order, stored, ct) =>
            {
                var deleted = await store.DeleteAsync(stored.Entry, version, ct);
                if (deleted.Status == StoreSaveStatus.Conflict)
                {
                    return new EntryWriteResult(EntryWriteOutcome.Conflict);
                }

                await auditLog.RecordAsync(PaymentAudit.Deleted(stored.Entry, order.Number, accountId), ct);
                return new EntryWriteResult(EntryWriteOutcome.Deleted, stored.Entry.Id);
            },
            cancellationToken);

    private Task<EntryWriteResult> ChangeAsync(
        Guid orderId,
        Guid entryId,
        byte[] version,
        Func<TEntry, EntryTotals, PaidRuleViolation?>? check,
        Func<LockedOrder, StoredEntry<TEntry>, CancellationToken, Task<EntryWriteResult>> apply,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);

        return unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var order = await orderLock.LockAsync(orderId, ct);
                if (order is null)
                {
                    return new EntryWriteResult(EntryWriteOutcome.OrderNotFound);
                }

                var stored = await store.LoadAsync(entryId, ct);
                if (stored is null || stored.Entry.OrderId != orderId)
                {
                    return new EntryWriteResult(EntryWriteOutcome.EntryNotFound);
                }

                if (!stored.Version.AsSpan().SequenceEqual(version))
                {
                    return new EntryWriteResult(EntryWriteOutcome.Conflict);
                }

                if (check?.Invoke(stored.Entry, order.Totals) is { } violation)
                {
                    return Rejected(violation);
                }

                return await apply(order, stored, ct);
            },
            commitWhen: result => result.Outcome is EntryWriteOutcome.Saved or EntryWriteOutcome.Deleted,
            cancellationToken);
    }

    private static EntryWriteResult Resolve(EntryCreationMatch existing, byte[] requestHash) =>
        existing.RequestHash.AsSpan().SequenceEqual(requestHash)
            ? new EntryWriteResult(EntryWriteOutcome.Repeated, existing.Id, existing.Version)
            : new EntryWriteResult(EntryWriteOutcome.KeyReused, existing.Id);

    private static EntryWriteResult Rejected(PaidRuleViolation violation) => violation.Kind switch
    {
        PaidRuleViolationKind.RefundExceedsPaid => new EntryWriteResult(EntryWriteOutcome.RefundExceedsPaid, Available: violation.Available),
        _ => new EntryWriteResult(EntryWriteOutcome.PaidWouldBeNegative),
    };

    private sealed record AddAttempt(EntryWriteResult? Result, Exception? Duplicate);
}
