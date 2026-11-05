using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Orders;
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
}

public sealed record EditOrderResult(
    EditOrderOutcome Outcome,
    byte[]? Version = null,
    FieldErrors? Errors = null,
    IReadOnlyList<ShopOwnedField>? Fields = null);

/// <summary>
/// Kolejność sprawdzeń (plan SF-8, N1): walidacja niezależna od źródła (400), istnienie (404),
/// wersja (409), walidacja zależna od źródła — wymagalność w zamówieniu ręcznym (400) albo
/// porównanie pól sklepu (422) — reguły statusu (422), brak zmian (sukces bez zapisu), zapis i
/// audyt <c>Updated</c>. Całość w jednej jednostce pracy, zatwierdzanej tylko przy zapisie.
/// </summary>
public sealed class EditOrderHandler(
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    IOrderStore store,
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
            edit = new OrderEdit(
                editable.FulfillmentStatus, editable.Operational, editable.Comment, shop.OrderDate, shop.Channel, shop.Value, shop.Customer);

            // Pole podane w postaci nieczytelnej nie może być równe zapisanemu: też jest zmianą pola sklepu.
            var differing = order.ShopFieldsChangedBy(edit).Union(shop.Unreadable).ToHashSet();
            if (differing.Count > 0)
            {
                return new EditOrderResult(
                    EditOrderOutcome.FieldOwnedByShop,
                    Fields: [.. Order.AllShopOwnedFields.Where(differing.Contains)]);
            }
        }

        var result = order.Edit(edit, timeProvider.GetUtcNow().UtcDateTime);
        switch (result.Violation?.Kind)
        {
            case OrderRuleViolationKind.FieldOwnedByShop:
                return new EditOrderResult(EditOrderOutcome.FieldOwnedByShop, Fields: result.Violation.Fields);
            case OrderRuleViolationKind.StatusChangeRequiresAction:
                return new EditOrderResult(EditOrderOutcome.StatusChangeRequiresAction);
            case not null:
                throw new InvalidOperationException($"Nieoczekiwane naruszenie reguły edycji: {result.Violation.Kind}.");
        }

        if (result.Changes.Count == 0)
        {
            return new EditOrderResult(EditOrderOutcome.Saved, stored.Version);
        }

        var saved = await store.SaveAsync(order, version, cancellationToken);
        if (saved.Status == OrderSaveStatus.Conflict)
        {
            return new EditOrderResult(EditOrderOutcome.Conflict);
        }

        await auditLog.RecordAsync(OrderAudit.Updated(order, result.Changes, command.AccountId), cancellationToken);
        return new EditOrderResult(EditOrderOutcome.Saved, saved.Version);
    }
}
