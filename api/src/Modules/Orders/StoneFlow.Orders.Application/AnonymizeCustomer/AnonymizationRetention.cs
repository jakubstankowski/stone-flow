using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Orders.Application.AnonymizeCustomer;

/// <summary>
/// Wspólna operacja zapytania szczegółów i komendy anonimizacji (SF-22): jedyne miejsce zebrania faktów
/// retencji (konfiguracja, wpisy zamówienia, ostatnia aktywność w audycie — także usunięcia wpisów) i
/// przekazania ich regule domeny <see cref="CustomerAnonymization"/>. Reguła należy do domeny.
/// </summary>
public sealed class AnonymizationRetention(
    IAnonymizationSettings settings,
    IOrderAnonymizationStore store,
    IAuditActivity auditActivity,
    TimeProvider timeProvider)
{
    /// <summary>Okres retencji z konfiguracji; <c>null</c> = funkcja wyłączona.</summary>
    public int? RetentionMonths => settings.RetentionMonths;

    /// <summary>
    /// Stan akcji. Dla zamówienia zanonimizowanego i przy wyłączonej funkcji bez odczytów; inaczej po kolei
    /// (wspólne połączenie) fakty wpisów i czas ostatniego wpisu audytu zamówienia i jego wpisów.
    /// </summary>
    public async Task<AnonymizationAvailability> EvaluateAsync(
        Guid orderId,
        DateOnly orderDate,
        DateTime orderUpdatedAtUtc,
        bool isAnonymized,
        CancellationToken cancellationToken)
    {
        var today = WarsawTime.Today(timeProvider.GetUtcNow());
        var months = settings.RetentionMonths;
        if (isAnonymized || months is null)
        {
            return CustomerAnonymization.Evaluate(isAnonymized, months, null, today);
        }

        var facts = await store.GetRetentionFactsAsync(orderId, cancellationToken);
        var auditAt = await auditActivity.LastRecordedAtUtcAsync(new AuditObjectReference(AuditObjectType.Order, orderId), cancellationToken);
        var lastChange = Latest(Latest(orderUpdatedAtUtc, facts.LastEntryChangeUtc), auditAt);
        var start = CustomerAnonymization.RetentionStart(orderDate, facts.LastEntryDate, WarsawTime.DateOf(lastChange));
        return CustomerAnonymization.Evaluate(false, months, start, today);
    }

    private static DateTime Latest(DateTime value, DateTime? other) => other is { } candidate && candidate > value ? candidate : value;
}
