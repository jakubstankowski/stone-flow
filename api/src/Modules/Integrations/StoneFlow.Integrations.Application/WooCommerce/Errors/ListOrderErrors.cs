using System.Globalization;
using StoneFlow.Integrations.Domain.WooCommerce;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.ShopOrders;

namespace StoneFlow.Integrations.Application.WooCommerce.Errors;

/// <summary>Błąd zamówienia z zamówieniem ERP (jeśli istnieje).</summary>
/// <param name="Error">Błąd z historii.</param>
/// <param name="OrderId">Id zamówienia w ERP.</param>
/// <param name="OrderNumber">Numer zamówienia w ERP.</param>
public sealed record OrderErrorItem(OrderErrorView Error, Guid? OrderId, string? OrderNumber)
{
    /// <summary>Łączy błędy z zamówieniami ERP jednym wyszukaniem po ID ze sklepu.</summary>
    public static async Task<IReadOnlyList<OrderErrorItem>> WithOrdersAsync(
        IReadOnlyList<OrderErrorView> errors,
        IShopOrderLookup lookup,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(lookup);
        var orders = await lookup.FindAsync([.. errors.Select(e => e.ExternalId).Distinct(StringComparer.Ordinal)], cancellationToken);
        return
        [
            .. errors.Select(e => orders.TryGetValue(e.ExternalId, out var order)
                ? new OrderErrorItem(e, order.Id, order.Number)
                : new OrderErrorItem(e, null, null)),
        ];
    }
}

/// <summary>Strona historii błędów.</summary>
public sealed record OrderErrorListPage(IReadOnlyList<OrderErrorItem> Items, int Page, int PageSize, int Total);

/// <summary>Lista błędów: <c>status</c> = <c>open</c> (domyślnie), <c>closed</c> albo <c>all</c>.</summary>
/// <param name="Status">Filtr statusu.</param>
/// <param name="Page">Numer strony od 1 (tekst z adresu).</param>
/// <param name="PageSize">Rozmiar strony 1–100, domyślnie 25 (tekst z adresu).</param>
public sealed record ListOrderErrorsQuery(string? Status, string? Page, string? PageSize) : IQuery<ListOrderErrorsResult>;

/// <summary>Strona albo błędy pól (<c>400</c>).</summary>
public sealed record ListOrderErrorsResult(OrderErrorListPage? Page, IReadOnlyDictionary<string, string[]>? Errors);

public sealed class ListOrderErrorsHandler(IWooCommerceOrderErrors errors, IShopOrderLookup lookup)
    : IQueryHandler<ListOrderErrorsQuery, ListOrderErrorsResult>
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public async Task<ListOrderErrorsResult> HandleAsync(ListOrderErrorsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var fieldErrors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var status = string.IsNullOrWhiteSpace(query.Status) ? OrderErrorStatus.Open : query.Status.Trim();
        if (status is not (OrderErrorStatus.Open or OrderErrorStatus.Closed or "all"))
        {
            fieldErrors["status"] = ["Dozwolone wartości: open, closed, all."];
        }

        var page = Parse(query.Page, 1);
        if (page is not >= 1)
        {
            fieldErrors["page"] = ["Numer strony musi być liczbą całkowitą od 1."];
        }

        var pageSize = Parse(query.PageSize, DefaultPageSize);
        if (pageSize is not (>= 1 and <= MaxPageSize))
        {
            fieldErrors["pageSize"] = [$"Rozmiar strony musi być liczbą całkowitą od 1 do {MaxPageSize}."];
        }

        if (fieldErrors.Count > 0)
        {
            return new ListOrderErrorsResult(null, fieldErrors);
        }

        var result = await errors.ListAsync(status == "all" ? null : status, page!.Value, pageSize!.Value, cancellationToken);
        var items = await OrderErrorItem.WithOrdersAsync(result.Items, lookup, cancellationToken);
        return new ListOrderErrorsResult(new OrderErrorListPage(items, page.Value, pageSize.Value, result.Total), null);
    }

    /// <summary>Liczba z adresu; brak → wartość domyślna; tekst nieliczbowy → <c>null</c>.</summary>
    private static int? Parse(string? text, int fallback) =>
        string.IsNullOrWhiteSpace(text)
            ? fallback
            : int.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null;
}
