using System.Globalization;
using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Orders.Application.ListOrders;

/// <summary>
/// Pozycja listy. Sumy wpisów liczy baza (<c>COALESCE(SUM(...), 0)</c>), a Zapłacono i stan
/// wylicza <see cref="PaymentSummary"/> — ta sama reguła co w szczegółach (N5).
/// </summary>
public sealed record OrderListItemDto(
    Guid Id,
    string Number,
    DateOnly OrderDate,
    string CustomerName,
    OrderChannel Channel,
    OrderSource Source,
    decimal Value,
    FulfillmentStatus FulfillmentStatus,
    decimal PaymentsTotal,
    decimal RefundsTotal)
{
    public PaymentSummary Payment =>
        PaymentSummary.Compute(Value, FulfillmentStatus == FulfillmentStatus.Cancelled, PaymentsTotal, RefundsTotal);
}

/// <summary>Jedna strona listy; <see cref="Total"/> to liczba wszystkich zamówień spełniających filtry.</summary>
public sealed record OrderListPageDto(IReadOnlyList<OrderListItemDto> Items, int Page, int PageSize, int Total);

public enum OrderSort
{
    OrderDate,
    Number,
    Customer,
    Value,
}

/// <summary>
/// Zwalidowane kryteria listy; rozstrzygnięcie remisu po <c>Id</c> dodaje odczyt. Filtr stanu
/// płatności: <see cref="PaymentStates"/> oraz <see cref="WithoutPaymentState"/> (zamówienia
/// anulowane) łączone przez OR; oba puste oznaczają brak filtra.
/// </summary>
public sealed record OrderListCriteria(
    int Page,
    int PageSize,
    OrderSort Sort,
    bool Descending,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    OrderChannel? Channel,
    OrderSource? Source,
    IReadOnlyList<FulfillmentStatus> Statuses,
    string? Search,
    IReadOnlyList<PaymentState> PaymentStates,
    bool WithoutPaymentState);

/// <summary>Parametry listy z zapytania HTTP (tekst); walidacja w handlerze.</summary>
public sealed record ListOrdersQuery(
    string? Page = null,
    string? PageSize = null,
    string? Sort = null,
    string? Direction = null,
    string? DateFrom = null,
    string? DateTo = null,
    string? Channel = null,
    string? Source = null,
    IReadOnlyList<string>? Status = null,
    string? Search = null,
    IReadOnlyList<string>? PaymentState = null) : IQuery<ListOrdersResult>;

/// <summary>Strona listy albo błędy parametrów (400).</summary>
public sealed record ListOrdersResult(OrderListPageDto? Page, FieldErrors? Errors = null);

public sealed class ListOrdersHandler(IOrderReader reader) : IQueryHandler<ListOrdersQuery, ListOrdersResult>
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;
    public const int SearchMaxLength = 100;

    /// <summary>Wartość filtra stanu płatności dla zamówień bez stanu (anulowanych).</summary>
    public const string WithoutPaymentStateCode = "none";

    private static readonly Dictionary<string, OrderSort> Sorts = new(StringComparer.Ordinal)
    {
        ["orderDate"] = OrderSort.OrderDate,
        ["number"] = OrderSort.Number,
        ["customer"] = OrderSort.Customer,
        ["value"] = OrderSort.Value,
    };

    public async Task<ListOrdersResult> HandleAsync(ListOrdersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new FieldErrors();
        var criteria = Validate(query, errors);
        if (criteria is null)
        {
            return new ListOrdersResult(null, errors);
        }

        return new ListOrdersResult(await reader.ListAsync(criteria, cancellationToken));
    }

    /// <summary>
    /// Domyślnie: strona 1, 25 pozycji, data zamówienia malejąco. Podany <c>sort</c> bez
    /// <c>direction</c> sortuje rosnąco.
    /// </summary>
    private static OrderListCriteria? Validate(ListOrdersQuery query, FieldErrors errors)
    {
        var page = 1;
        if (OrderInput.Text(query.Page) is { } pageText && (!int.TryParse(pageText, NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1))
        {
            errors.Add("page", "Numer strony musi być liczbą całkowitą od 1.");
        }

        var pageSize = DefaultPageSize;
        if (OrderInput.Text(query.PageSize) is { } sizeText
            && (!int.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out pageSize) || pageSize is < 1 or > MaxPageSize))
        {
            errors.Add("pageSize", $"Rozmiar strony musi być liczbą od 1 do {MaxPageSize}.");
        }

        var sort = OrderSort.OrderDate;
        var sortText = OrderInput.Text(query.Sort);
        if (sortText is not null && !Sorts.TryGetValue(sortText, out sort))
        {
            errors.Add("sort", "Nieznane pole sortowania.");
        }

        var descending = sortText is null;
        switch (OrderInput.Text(query.Direction))
        {
            case null:
                break;
            case "asc":
                descending = false;
                break;
            case "desc":
                descending = true;
                break;
            default:
                errors.Add("direction", "Kierunek sortowania to asc albo desc.");
                break;
        }

        var dateFrom = ParseDate(query.DateFrom, "dateFrom", errors);
        var dateTo = ParseDate(query.DateTo, "dateTo", errors);
        if (dateFrom > dateTo)
        {
            errors.Add("dateTo", "Data końcowa nie może być wcześniejsza niż początkowa.");
        }

        OrderChannel? channel = null;
        if (OrderInput.Text(query.Channel) is { } channelCode)
        {
            if (OrderCodes.TryParse(channelCode, out OrderChannel parsed))
            {
                channel = parsed;
            }
            else
            {
                errors.Add("channel", OrderMessages.ChannelUnknown);
            }
        }

        OrderSource? source = null;
        if (OrderInput.Text(query.Source) is { } sourceCode)
        {
            if (OrderCodes.TryParse(sourceCode, out OrderSource parsed))
            {
                source = parsed;
            }
            else
            {
                errors.Add("source", "Nieznane źródło zamówienia.");
            }
        }

        var statuses = new List<FulfillmentStatus>();
        foreach (var code in (query.Status ?? []).Select(OrderInput.Text).OfType<string>())
        {
            if (OrderCodes.TryParse(code, out FulfillmentStatus status))
            {
                statuses.Add(status);
            }
            else if (!errors.Contains("status"))
            {
                errors.Add("status", OrderMessages.StatusUnknown);
            }
        }

        var paymentStates = new List<PaymentState>();
        var withoutPaymentState = false;
        foreach (var code in (query.PaymentState ?? []).Select(OrderInput.Text).OfType<string>())
        {
            if (string.Equals(code, WithoutPaymentStateCode, StringComparison.Ordinal))
            {
                withoutPaymentState = true;
            }
            else if (OrderCodes.TryParse(code, out PaymentState state))
            {
                paymentStates.Add(state);
            }
            else if (!errors.Contains("paymentState"))
            {
                errors.Add("paymentState", "Nieznany stan płatności.");
            }
        }

        var search = OrderInput.Text(query.Search);
        if (search is { Length: > SearchMaxLength })
        {
            errors.Add("search", $"Wyszukiwany tekst może mieć najwyżej {SearchMaxLength} znaków.");
        }

        return errors.IsEmpty
            ? new OrderListCriteria(
                page, pageSize, sort, descending, dateFrom, dateTo, channel, source, [.. statuses.Distinct()], search, [.. paymentStates.Distinct()], withoutPaymentState)
            : null;
    }

    private static DateOnly? ParseDate(string? text, string field, FieldErrors errors)
    {
        if (OrderInput.Text(text) is not { } trimmed)
        {
            return null;
        }

        if (DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        errors.Add(field, "Podaj datę w formacie RRRR-MM-DD.");
        return null;
    }
}
