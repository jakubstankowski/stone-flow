using StoneFlow.Integrations.Domain.WooCommerce;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.ShopOrders;

namespace StoneFlow.Integrations.Application.WooCommerce.Errors;

/// <summary>Wynik „Ponów”.</summary>
public enum RetryOrderErrorOutcome
{
    /// <summary><c>200</c>: zapis się odbył; błąd zamknięty albo (warunek wersji) nadal otwarty.</summary>
    Done,

    /// <summary><c>404</c>: brak błędu.</summary>
    NotFound,

    /// <summary><c>409</c> <c>error_closed</c>.</summary>
    Closed,

    /// <summary><c>409</c> <c>integration_not_configured</c>.</summary>
    NotConfigured,

    /// <summary><c>502</c> <c>retry_failed</c>: sklep nie dał czytelnego zamówienia; błąd otwarty.</summary>
    ShopFailed,

    /// <summary><c>503</c> <c>retry_unavailable</c>: błąd zapisu w ERP; błąd otwarty.</summary>
    Unavailable,
}

/// <summary>„Ponów” błędu zamówienia (SF-13): pobranie jednego zamówienia z REST i zapis.</summary>
/// <param name="ErrorId">Id błędu.</param>
/// <param name="CorrelationId">Identyfikator korelacji żądania (kontekst w historii); brak → nowy.</param>
public sealed record RetryOrderErrorCommand(Guid ErrorId, string? CorrelationId) : ICommand<RetryOrderErrorResult>;

/// <summary>Wynik z błędem po zmianie dla <see cref="RetryOrderErrorOutcome.Done"/>.</summary>
public sealed record RetryOrderErrorResult(RetryOrderErrorOutcome Outcome, OrderErrorItem? Error = null);

/// <summary>
/// „Ponów” niezależnie od dzierżawy uzupełniania (blokada SF-12 po ID szereguje zapis). <c>404</c>
/// z kodem nieistniejącego zamówienia stosuje usunięcie w sklepie; każda inna odpowiedź sklepu
/// zostawia błąd otwarty z kolejną próbą.
/// </summary>
public sealed class RetryOrderErrorHandler(
    IWooCommerceOrderErrors errors,
    IWooCommerceRestSettings settings,
    IWooCommerceRestClient client,
    OrderOutcome outcome,
    IShopOrderLookup lookup) : ICommandHandler<RetryOrderErrorCommand, RetryOrderErrorResult>
{
    public async Task<RetryOrderErrorResult> HandleAsync(RetryOrderErrorCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await errors.GetAsync(command.ErrorId, cancellationToken) is not { } error)
        {
            return new RetryOrderErrorResult(RetryOrderErrorOutcome.NotFound);
        }

        if (error.Status == OrderErrorStatus.Closed)
        {
            return new RetryOrderErrorResult(RetryOrderErrorOutcome.Closed);
        }

        if (!settings.IsConfigured)
        {
            return new RetryOrderErrorResult(RetryOrderErrorOutcome.NotConfigured);
        }

        var context = string.IsNullOrWhiteSpace(command.CorrelationId) ? Guid.NewGuid().ToString("N") : command.CorrelationId.Trim();
        var fetched = await client.GetOrderAsync(error.ExternalId, cancellationToken);
        OrderApplyResult applied;
        switch (fetched)
        {
            case RestOrderResult.Found found:
                applied = await outcome.ApplyBodyAsync(ShopOrderTopic.Updated, found.Body, OrderErrorSource.Retry, context, cancellationToken);
                break;
            case RestOrderResult.NotFound:
                applied = await outcome.ApplyAsync(ShopOrderSnapshot.Deletion(error.ExternalId), OrderErrorSource.Retry, context, cancellationToken);
                break;
            default:
                var message = ((RestOrderResult.Failed)fetched).Message;
                return await ShopFailedAsync(new OrderFailure(error.ExternalId, OrderErrorSource.Retry, message, context, null), cancellationToken);
        }

        // Próba, której nie udało się zapisać w historii, to błąd zapisu w ERP: 503, błąd bez zmian.
        if (applied.ErrorNotRecorded)
        {
            return new RetryOrderErrorResult(RetryOrderErrorOutcome.Unavailable);
        }

        switch (applied.Status)
        {
            case OrderApplyStatus.Applied:
                break;
            case OrderApplyStatus.Failed:
                return new RetryOrderErrorResult(RetryOrderErrorOutcome.Unavailable);
            case OrderApplyStatus.Unidentified:
                return await ShopFailedAsync(
                    new OrderFailure(error.ExternalId, OrderErrorSource.Retry, "Nieczytelna treść zamówienia: brak id zamówienia", context, null),
                    cancellationToken);
            default:
                return new RetryOrderErrorResult(RetryOrderErrorOutcome.ShopFailed);
        }

        var after = await errors.GetAsync(command.ErrorId, cancellationToken) ?? error;
        var item = (await OrderErrorItem.WithOrdersAsync([after], lookup, cancellationToken))[0];
        return new RetryOrderErrorResult(RetryOrderErrorOutcome.Done, item);
    }

    /// <summary>Sklep nie dał zamówienia: kolejna próba w historii i <c>502</c>; nieudany zapis historii → <c>503</c>.</summary>
    private async Task<RetryOrderErrorResult> ShopFailedAsync(OrderFailure failure, CancellationToken cancellationToken) =>
        new(await outcome.RecordAsync(failure, cancellationToken) ? RetryOrderErrorOutcome.ShopFailed : RetryOrderErrorOutcome.Unavailable);
}
