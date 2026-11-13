using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.Persistence;

namespace StoneFlow.Orders.Application.AnonymizeCustomer;

/// <summary>„Anonimizuj dane klienta” (PRD §5, DANE-01, SF-22); <paramref name="Version"/> zamówienia (base64).</summary>
public sealed record AnonymizeCustomerCommand(Guid AccountId, Guid OrderId, string? Version) : ICommand<AnonymizeCustomerResult>;

public enum AnonymizeCustomerOutcome
{
    Saved,
    ValidationFailed,

    /// <summary>Brak okresu retencji w konfiguracji (funkcja wyłączona do decyzji D-09).</summary>
    Disabled,
    NotFound,
    Conflict,
    AlreadyAnonymized,

    /// <summary>Okres retencji nie upłynął; <c>AvailableFrom</c> to pierwszy dzień dostępności.</summary>
    RetentionNotElapsed,
}

public sealed record AnonymizeCustomerResult(
    AnonymizeCustomerOutcome Outcome,
    byte[]? Version = null,
    FieldErrors? Errors = null,
    DateOnly? AvailableFrom = null);

/// <summary>
/// Anonimizacja danych klienta zamówienia, jego zwrotów i korekt oraz ich audytu, w jednej jednostce pracy
/// zatwierdzanej tylko przy zapisie. Kolejność kontroli: wersja (400), funkcja wyłączona (422, bez dostępu
/// do bazy — także dla nieistniejącego zamówienia), w transakcji: blokada i istnienie (404), wersja (409),
/// stan (409 już zanonimizowane), retencja liczona po blokadzie (422). Blokada <see cref="IOrderLock"/>
/// szereguje operację z zapisami wpisów. Synchronizacja ze sklepem celowo jej nie bierze (szereguje ją
/// blokada po ID zewnętrznym); spójność z nią daje <c>rowversion</c> zamówienia: przestarzały zapis
/// synchronizacji jest ponawiany na zanonimizowanym zamówieniu, a przestarzała anonimizacja dostaje 409.
/// Handler nie zapisuje <c>Updated</c> ze starymi wartościami (to dane osobowe) i niczego nie loguje.
/// </summary>
public sealed class AnonymizeCustomerHandler(
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    IAuditRedaction auditRedaction,
    IOrderLock orderLock,
    IOrderStore store,
    IOrderAnonymizationStore anonymizationStore,
    AnonymizationRetention retention,
    TimeProvider timeProvider) : ICommandHandler<AnonymizeCustomerCommand, AnonymizeCustomerResult>
{
    public async Task<AnonymizeCustomerResult> HandleAsync(AnonymizeCustomerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new FieldErrors();
        if (OrderVersion.Validate(command.Version, errors) is not { } version)
        {
            return new AnonymizeCustomerResult(AnonymizeCustomerOutcome.ValidationFailed, Errors: errors);
        }

        if (retention.RetentionMonths is not { } months)
        {
            return new AnonymizeCustomerResult(AnonymizeCustomerOutcome.Disabled);
        }

        return await unitOfWork.ExecuteInTransactionAsync(
            ct => AnonymizeAsync(command, version, months, ct),
            commitWhen: result => result.Outcome == AnonymizeCustomerOutcome.Saved,
            cancellationToken);
    }

    private async Task<AnonymizeCustomerResult> AnonymizeAsync(
        AnonymizeCustomerCommand command, byte[] version, int months, CancellationToken cancellationToken)
    {
        if (await orderLock.LockAsync(command.OrderId, cancellationToken) is null)
        {
            return new AnonymizeCustomerResult(AnonymizeCustomerOutcome.NotFound);
        }

        var stored = await store.LoadAsync(command.OrderId, cancellationToken);
        if (stored is null)
        {
            return new AnonymizeCustomerResult(AnonymizeCustomerOutcome.NotFound);
        }

        if (!stored.Version.AsSpan().SequenceEqual(version))
        {
            return new AnonymizeCustomerResult(AnonymizeCustomerOutcome.Conflict);
        }

        var order = stored.Order;
        if (order.IsCustomerAnonymized)
        {
            return new AnonymizeCustomerResult(AnonymizeCustomerOutcome.AlreadyAnonymized);
        }

        var availability = await retention.EvaluateAsync(order.Id, order.OrderDate, order.UpdatedAtUtc, isAnonymized: false, cancellationToken);
        if (availability.Status != AnonymizationStatus.Available)
        {
            return new AnonymizeCustomerResult(AnonymizeCustomerOutcome.RetentionNotElapsed, AvailableFrom: availability.AvailableFrom);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var changes = order.AnonymizeCustomer(command.AccountId, nowUtc);
        if (!changes.Succeeded)
        {
            return new AnonymizeCustomerResult(AnonymizeCustomerOutcome.AlreadyAnonymized);
        }

        var entries = await anonymizationStore.LoadReasonEntriesAsync(order.Id, cancellationToken);
        var changedEntries = new List<OrderEntry>();
        foreach (var refund in entries.Refunds)
        {
            if (refund.AnonymizeReason(nowUtc).Count > 0)
            {
                changedEntries.Add(refund);
            }
        }

        foreach (var correction in entries.SalesCorrections)
        {
            if (correction.AnonymizeReason(nowUtc).Count > 0)
            {
                changedEntries.Add(correction);
            }
        }

        var saved = await store.SaveAsync(order, version, cancellationToken);
        if (saved.Status == StoreSaveStatus.Conflict)
        {
            return new AnonymizeCustomerResult(AnonymizeCustomerOutcome.Conflict);
        }

        await auditRedaction.RedactAsync(OrderAnonymizationAudit.Redaction(order.Id), cancellationToken);

        // Data początku istnieje zawsze dla stanu Available (kontrakt CustomerAnonymization.Evaluate).
        await auditLog.RecordAsync(
            OrderAnonymizationAudit.Anonymized(
                order,
                [.. changes.Changes.Select(change => change.Field)],
                changedEntries.Count,
                availability.RetentionStartDate!.Value,
                months,
                command.AccountId),
            cancellationToken);
        foreach (var entry in changedEntries)
        {
            await auditLog.RecordAsync(OrderAnonymizationAudit.EntryAnonymized(entry, order.Number, command.AccountId), cancellationToken);
        }

        return new AnonymizeCustomerResult(AnonymizeCustomerOutcome.Saved, saved.Version);
    }
}
