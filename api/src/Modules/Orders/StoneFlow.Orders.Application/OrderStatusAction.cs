using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Application.Persistence;

namespace StoneFlow.Orders.Application;

public enum OrderStatusActionOutcome
{
    Saved,
    ValidationFailed,
    NotFound,
    Conflict,

    /// <summary>Akcja niezgodna ze stanem (anulowanie anulowanego, przywrócenie nieanulowanego).</summary>
    InvalidState,
}

public sealed record OrderStatusActionResult(OrderStatusActionOutcome Outcome, byte[]? Version = null, FieldErrors? Errors = null);

/// <summary>
/// Wspólny przebieg anulowania i przywrócenia (właściciel: handlery <c>CancelOrder</c> i
/// <c>RestoreOrder</c>). W jednej jednostce pracy: istnienie (404), wersja (409), stan (409), zapis
/// z kontrolą wersji i audyt <c>Updated</c> statusu. Zatwierdzany jest tylko zapis.
/// </summary>
public sealed class OrderStatusAction(IUnitOfWork unitOfWork, IAuditLog auditLog, IOrderStore store, TimeProvider timeProvider)
{
    public async Task<OrderStatusActionResult> ExecuteAsync(
        Guid accountId,
        Guid orderId,
        string? versionText,
        Func<Order, DateTime, OrderChangeResult> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        var errors = new FieldErrors();
        if (OrderVersion.Validate(versionText, errors) is not { } version)
        {
            return new OrderStatusActionResult(OrderStatusActionOutcome.ValidationFailed, Errors: errors);
        }

        return await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var stored = await store.LoadAsync(orderId, ct);
                if (stored is null)
                {
                    return new OrderStatusActionResult(OrderStatusActionOutcome.NotFound);
                }

                if (!stored.Version.AsSpan().SequenceEqual(version))
                {
                    return new OrderStatusActionResult(OrderStatusActionOutcome.Conflict);
                }

                var result = action(stored.Order, timeProvider.GetUtcNow().UtcDateTime);
                if (!result.Succeeded)
                {
                    return new OrderStatusActionResult(OrderStatusActionOutcome.InvalidState);
                }

                var saved = await store.SaveAsync(stored.Order, version, ct);
                if (saved.Status == OrderSaveStatus.Conflict)
                {
                    return new OrderStatusActionResult(OrderStatusActionOutcome.Conflict);
                }

                await auditLog.RecordAsync(OrderAudit.Updated(stored.Order, result.Changes, accountId), ct);
                return new OrderStatusActionResult(OrderStatusActionOutcome.Saved, saved.Version);
            },
            commitWhen: result => result.Outcome == OrderStatusActionOutcome.Saved,
            cancellationToken);
    }
}
