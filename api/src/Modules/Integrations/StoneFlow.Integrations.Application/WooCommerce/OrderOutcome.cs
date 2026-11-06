using StoneFlow.Integrations.Domain.WooCommerce;
using StoneFlow.Shared.Application.ShopOrders;

namespace StoneFlow.Integrations.Application.WooCommerce;

/// <summary>Wynik zastosowania jednego zamówienia przez <see cref="OrderOutcome"/>.</summary>
public enum OrderApplyStatus
{
    /// <summary>Zapis się udał (także pominięcie); otwarty błąd zamknięty, jeśli pozwalał warunek wersji.</summary>
    Applied,

    /// <summary>Treść bez czytelnego ID: brak klucza błędu, tylko log.</summary>
    Unidentified,

    /// <summary>Treść nieczytelna z ID: błąd zamówienia zapisany (albo nie — <see cref="OrderApplyResult.ErrorNotRecorded"/>).</summary>
    Unreadable,

    /// <summary>Błąd zapisu w ERP (wyjątek albo dwa razy <c>RetryRequired</c>): błąd zamówienia zapisany (albo nie).</summary>
    Failed,
}

/// <summary>Wynik zastosowania zamówienia.</summary>
/// <param name="Status">Rodzaj wyniku.</param>
/// <param name="ExternalId">ID zamówienia, jeśli znane.</param>
/// <param name="SyncResult">Wynik <see cref="IShopOrderSync"/>, gdy zapis się odbył.</param>
/// <param name="ErrorNotRecorded">
/// Niepowodzenie nie trafiło do historii błędów (sam zapis historii się nie udał; zalogowane). Wywołujący
/// decyduje o skutkach: uzupełnianie kończy przebieg niepowodzeniem, „Ponów” zwraca <c>503</c>,
/// webhook nie zmienia kodu odpowiedzi.
/// </param>
public sealed record OrderApplyResult(OrderApplyStatus Status, string? ExternalId, ShopOrderSyncResult? SyncResult = null, bool ErrorNotRecorded = false);

/// <summary>
/// Zastosowanie jednego zamówienia wspólne dla webhooka, uzupełniania i „Ponów” (SF-13): zapis przez
/// <see cref="IShopOrderSyncRunner"/>, a potem zamknięcie albo zapis błędu w historii. Błąd zapisu
/// zamówienia i błąd zapisu historii logowane są tu, raz.
/// </summary>
public sealed class OrderOutcome(IShopOrderSyncRunner sync, IWooCommerceOrderErrors errors, IWooCommerceSyncEvents events)
{
    /// <summary>Odczytuje treść i stosuje zamówienie; nieczytelna treść z ID daje błąd zamówienia.</summary>
    /// <param name="topic">Temat dostawy.</param>
    /// <param name="body">Surowa treść zamówienia (JSON REST v3).</param>
    /// <param name="source">Źródło błędu (<see cref="OrderErrorSource"/>).</param>
    /// <param name="context">Kontekst do historii błędów.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    public async Task<OrderApplyResult> ApplyBodyAsync(
        ShopOrderTopic topic,
        byte[] body,
        string source,
        string context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var read = WooCommerceOrderPayload.Read(topic, body);
        return read.Snapshot is { } snapshot
            ? await ApplyAsync(snapshot, source, context, cancellationToken)
            : await RecordUnreadableAsync(read, source, context, cancellationToken);
    }

    /// <summary>Zapisuje błąd nieczytelnej treści, jeśli da się odczytać ID zamówienia.</summary>
    /// <param name="read">Wynik odczytu bez zamówienia.</param>
    /// <param name="source">Źródło błędu (<see cref="OrderErrorSource"/>).</param>
    /// <param name="context">Kontekst do historii błędów.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    public async Task<OrderApplyResult> RecordUnreadableAsync(
        PayloadReadResult read,
        string source,
        string context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);
        var problem = read.Problem ?? "nieczytelna treść";
        if (read.ExternalId is not { } externalId)
        {
            events.Unidentified(source, context, problem);
            return new OrderApplyResult(OrderApplyStatus.Unidentified, null);
        }

        var recorded = await RecordAsync(
            new OrderFailure(externalId, source, $"Nieczytelna treść zamówienia: {problem}", context, read.ModifiedAtUtc),
            cancellationToken);
        return new OrderApplyResult(OrderApplyStatus.Unreadable, externalId, ErrorNotRecorded: !recorded);
    }

    /// <summary>Stosuje zamówienie; sukces zamyka otwarty błąd, niepowodzenie go zapisuje.</summary>
    /// <param name="snapshot">Zamówienie z dostawy.</param>
    /// <param name="source">Źródło błędu i zamknięcia (<see cref="OrderErrorSource"/>).</param>
    /// <param name="context">Kontekst do historii błędów.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    public async Task<OrderApplyResult> ApplyAsync(
        ShopOrderSnapshot snapshot,
        string source,
        string context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ShopOrderSyncResult result;
        try
        {
            result = await sync.ApplyAsync(snapshot, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            events.ApplyFailed(exception, source, snapshot.ExternalId);
            var recorded = await RecordAsync(Failure(snapshot, source, context, $"Błąd zapisu zamówienia ({exception.GetType().Name})"), cancellationToken);
            return new OrderApplyResult(OrderApplyStatus.Failed, snapshot.ExternalId, ErrorNotRecorded: !recorded);
        }

        if (result == ShopOrderSyncResult.RetryRequired)
        {
            events.ApplyFailed(null, source, snapshot.ExternalId);
            var recorded = await RecordAsync(Failure(snapshot, source, context, "Błąd zapisu zamówienia (konflikt równoczesnego zapisu)"), cancellationToken);
            return new OrderApplyResult(OrderApplyStatus.Failed, snapshot.ExternalId, result, ErrorNotRecorded: !recorded);
        }

        // Nieudane zamknięcie nie gubi danych (zamówienie zapisane); błąd zostaje otwarty do kolejnego sukcesu.
        try
        {
            await errors.CloseAsync(
                snapshot.ExternalId,
                snapshot.ModifiedAtUtc,
                snapshot.Topic == ShopOrderTopic.Deleted,
                source,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            events.ErrorRecordFailed(exception, snapshot.ExternalId);
        }

        return new OrderApplyResult(OrderApplyStatus.Applied, snapshot.ExternalId, result);
    }

    /// <summary>Zapis błędu do historii; własny błąd zapisu tylko do logu.</summary>
    /// <param name="failure">Nieudana próba.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    /// <returns><c>false</c>, gdy zapis historii się nie udał.</returns>
    public async Task<bool> RecordAsync(OrderFailure failure, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(failure);
        try
        {
            await errors.RecordAsync(failure, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            events.ErrorRecordFailed(exception, failure.ExternalId);
            return false;
        }
    }

    private static OrderFailure Failure(ShopOrderSnapshot snapshot, string source, string context, string message) =>
        new(snapshot.ExternalId, source, message, context, snapshot.ModifiedAtUtc);
}
