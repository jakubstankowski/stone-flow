using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.Persistence;

namespace StoneFlow.Orders.Application.EditOrder;

/// <summary>Poprawa danych zamówienia; <paramref name="Version"/> odczytana przy wczytaniu (base64).</summary>
public sealed record EditOrderCommand(Guid AccountId, Guid OrderId, OrderInput Input, string? Version) : ICommand<EditOrderResult>;

public enum EditOrderOutcome
{
    /// <summary>Zapisano zmiany albo nic się nie zmieniło (wersja bez zmian).</summary>
    Saved,
    ValidationFailed,
    NotFound,
    Conflict,
    FieldOwnedByShop,
    StatusChangeRequiresAction,

    /// <summary>Nowa wartość byłaby niższa od sumy obniżeń sprzedaży; <c>MinimumValue</c> to ta suma.</summary>
    SalesReductionLimitExceeded,

    /// <summary>Zmiana klienta zamówienia z zanonimizowanymi danymi (SF-22).</summary>
    CustomerAnonymized,
}

public sealed record EditOrderResult(
    EditOrderOutcome Outcome,
    byte[]? Version = null,
    FieldErrors? Errors = null,
    IReadOnlyList<ShopOwnedField>? Fields = null,
    decimal? MinimumValue = null);

/// <summary>
/// Kolejność sprawdzeń (plan SF-8, N1): walidacja niezależna od źródła (400), istnienie (404),
/// wersja (409), walidacja zależna od źródła — wymagalność w zamówieniu ręcznym (400) albo
/// porównanie pól sklepu (422) — klient zanonimizowany (422, SF-22), reguły statusu (422), przy obniżeniu wartości blokada zamówienia
/// i limit obniżeń sprzedaży (422, SF-10, plan N2), brak zmian (sukces bez zapisu), zapis i audyt
/// <c>Updated</c>. Całość w jednej jednostce pracy, zatwierdzanej tylko przy zapisie.
/// </summary>
public sealed class EditOrderHandler(
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    IOrderStore store,
    IOrderLock orderLock,
    TimeProvider timeProvider) : ICommandHandler<EditOrderCommand, EditOrderResult>
{
    public async Task<EditOrderResult> HandleAsync(EditOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Input, nameof(command));

        var errors = new FieldErrors();
        var version = OrderVersion.Validate(command.Version, errors);
        var editable = command.Input.ValidateEditable(errors);
        if (!errors.IsEmpty || editable is null || version is null)
        {
            return new EditOrderResult(EditOrderOutcome.ValidationFailed, Errors: errors);
        }

        return await unitOfWork.ExecuteInTransactionAsync(
            ct => EditAsync(command, editable, version, ct),
            commitWhen: result => result.Outcome == EditOrderOutcome.Saved,
            cancellationToken);
    }

    private async Task<EditOrderResult> EditAsync(
        EditOrderCommand command,
        EditableOrderFields editable,
        byte[] version,
        CancellationToken cancellationToken)
    {
        var stored = await store.LoadAsync(command.OrderId, cancellationToken);
        if (stored is null)
        {
            return new EditOrderResult(EditOrderOutcome.NotFound);
        }

        if (!stored.Version.AsSpan().SequenceEqual(version))
        {
            return new EditOrderResult(EditOrderOutcome.Conflict);
        }

        var order = stored.Order;
        OrderEdit edit;
        if (order.Source == OrderSource.Manual)
        {
            var errors = new FieldErrors();
            var manual = command.Input.ValidateManualFields(errors);
            if (manual is null)
            {
                return new EditOrderResult(EditOrderOutcome.ValidationFailed, Errors: errors);
            }

            edit = new OrderEdit(
                editable.FulfillmentStatus, editable.Operational, editable.Comment, manual.OrderDate, manual.Channel, manual.Value, manual.Customer);
        }
        else
        {
            var shop = command.Input.ReadShopFields();
            // Porównanie z zapisanymi (także pól nieczytelnych) należy do domeny (Order.Edit).
            edit = new OrderEdit(
                editable.FulfillmentStatus,
                editable.Operational,
                editable.Comment,
                shop.OrderDate,
                shop.Channel,
                shop.Value,
                shop.Customer,
                shop.Unreadable);
        }

        var result = order.Edit(edit, timeProvider.GetUtcNow().UtcDateTime);
        switch (result.Violation?.Kind)
        {
            case OrderRuleViolationKind.FieldOwnedByShop:
                return new EditOrderResult(EditOrderOutcome.FieldOwnedByShop, Fields: result.Violation.Fields);
            case OrderRuleViolationKind.StatusChangeRequiresAction:
                return new EditOrderResult(EditOrderOutcome.StatusChangeRequiresAction);
            case OrderRuleViolationKind.CustomerAnonymized:
                return new EditOrderResult(EditOrderOutcome.CustomerAnonymized);
            case not null:
                throw new InvalidOperationException($"Nieoczekiwane naruszenie reguły edycji: {result.Violation.Kind}.");
        }

        if (await ValueBelowReductionsAsync(command.OrderId, result.Changes, cancellationToken) is { } minimumValue)
        {
            return new EditOrderResult(EditOrderOutcome.SalesReductionLimitExceeded, MinimumValue: minimumValue);
        }

        if (result.Changes.Count == 0)
        {
            return new EditOrderResult(EditOrderOutcome.Saved, stored.Version);
        }

        var saved = await store.SaveAsync(order, version, cancellationToken);
        if (saved.Status == StoreSaveStatus.Conflict)
        {
            return new EditOrderResult(EditOrderOutcome.Conflict);
        }

        await auditLog.RecordAsync(OrderAudit.Updated(order, result.Changes, command.AccountId), cancellationToken);
        return new EditOrderResult(EditOrderOutcome.Saved, saved.Version);
    }

    /// <summary>
    /// Limit obniżeń przy ręcznym obniżeniu wartości: tylko wtedy bierze blokadę zamówienia, żeby nie
    /// serializować edycji pól operacyjnych, i liczy sumy po jej uzyskaniu. Blokada nie zmienia
    /// wersji, więc wersji klienta dalej pilnuje zapis (<c>409</c>). Zwraca minimalną wartość albo
    /// <c>null</c>, gdy limit jest spełniony.
    /// </summary>
    private async Task<decimal?> ValueBelowReductionsAsync(
        Guid orderId, IReadOnlyList<OrderFieldChange> changes, CancellationToken cancellationToken)
    {
        if (changes.FirstOrDefault(change => change.Field == OrderField.Value) is not { OldValue: decimal oldValue, NewValue: decimal newValue }
            || !SalesReductionRules.LowersValue(oldValue, newValue))
        {
            return null;
        }

        var locked = await orderLock.LockAsync(orderId, cancellationToken)
            ?? throw new InvalidOperationException("Zamówienie wczytane w tej jednostce pracy nie istnieje.");
        return SalesReductionRules.MinimumValueFor(oldValue, newValue, locked.Totals.Reductions);
    }
}
