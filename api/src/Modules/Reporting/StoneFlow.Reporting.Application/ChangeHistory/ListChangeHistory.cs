using System.Globalization;
using StoneFlow.Reporting.Application.Abstractions;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Reporting.Application.ChangeHistory;

/// <summary>Parametry listy „Historia zmian” z zapytania HTTP (tekst); walidacja w handlerze.</summary>
public sealed record ListChangeHistoryQuery(
    string? ChangedFrom = null,
    string? ChangedTo = null,
    string? BusinessFrom = null,
    string? BusinessTo = null,
    IReadOnlyList<string>? ObjectType = null,
    string? Direction = null,
    string? Page = null,
    string? PageSize = null) : IQuery<ListChangeHistoryResult>;

/// <summary>Strona listy albo błędy parametrów (400).</summary>
public sealed record ListChangeHistoryResult(ChangeHistoryPageDto? Page, FieldErrors? Errors = null);

/// <summary>
/// Lista „Historia zmian” (SF-18) tylko do odczytu: walidacja parametrów, zamiana okresu zmiany
/// (Europe/Warsaw) na przedział UTC i odczyt strony wpisów audytu przez port. Okres nie ma limitu
/// długości, bo lista jest stronicowana w bazie i niczego nie sumuje.
/// </summary>
public sealed class ListChangeHistoryHandler(IChangeHistoryReader reader)
    : IQueryHandler<ListChangeHistoryQuery, ListChangeHistoryResult>
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public async Task<ListChangeHistoryResult> HandleAsync(ListChangeHistoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new FieldErrors();
        var criteria = Validate(query, errors);
        if (criteria is null)
        {
            return new ListChangeHistoryResult(null, errors);
        }

        return new ListChangeHistoryResult(await reader.ListAsync(criteria, cancellationToken));
    }

    /// <summary>Domyślnie: wszystkie rodzaje, od najnowszych, strona 1, 25 wpisów.</summary>
    private static ChangeHistoryCriteria? Validate(ListChangeHistoryQuery query, FieldErrors errors)
    {
        var changedFrom = RequiredDate(query.ChangedFrom, "changedFrom", "Podaj początek okresu zmiany.", errors);
        var changedTo = RequiredDate(query.ChangedTo, "changedTo", "Podaj koniec okresu zmiany.", errors);
        if (changedTo == DateOnly.MaxValue)
        {
            // Przedział UTC kończy się o północy dnia następnego, którego dla 9999-12-31 nie ma.
            errors.Add("changedTo", "Podaj datę nie późniejszą niż 9998-12-31.");
            changedTo = null;
        }

        if (changedFrom > changedTo)
        {
            errors.Add("changedFrom", "Początek okresu zmiany nie może być późniejszy niż koniec.");
        }

        var businessFrom = OptionalDate(query.BusinessFrom, "businessFrom", errors);
        var businessTo = OptionalDate(query.BusinessTo, "businessTo", errors);
        if (businessFrom > businessTo)
        {
            errors.Add("businessFrom", "Data biznesowa „od” nie może być późniejsza niż „do”.");
        }

        var objectTypes = new HashSet<AuditObjectType>();
        foreach (var code in (query.ObjectType ?? []).Select(Text).OfType<string>())
        {
            if (ChangeHistoryCodes.TryParse(code, out var type))
            {
                objectTypes.Add(type);
            }
            else if (!errors.Contains("objectType"))
            {
                errors.Add("objectType", "Nieznany rodzaj obiektu.");
            }
        }

        var descending = true;
        switch (Text(query.Direction))
        {
            case null:
            case "desc":
                break;
            case "asc":
                descending = false;
                break;
            default:
                errors.Add("direction", "Kierunek sortowania to asc albo desc.");
                break;
        }

        var page = 1;
        if (Text(query.Page) is { } pageText
            && (!int.TryParse(pageText, NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1))
        {
            errors.Add("page", "Numer strony musi być liczbą całkowitą od 1.");
        }

        var pageSize = DefaultPageSize;
        if (Text(query.PageSize) is { } sizeText
            && (!int.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out pageSize) || pageSize is < 1 or > MaxPageSize))
        {
            errors.Add("pageSize", $"Rozmiar strony musi być liczbą od 1 do {MaxPageSize}.");
        }

        if (!errors.IsEmpty || changedFrom is not { } from || changedTo is not { } to)
        {
            return null;
        }

        var (fromUtc, toUtcExclusive) = ChangeHistoryPeriod.ToUtcRange(from, to);
        return new ChangeHistoryCriteria(fromUtc, toUtcExclusive, businessFrom, businessTo, objectTypes, descending, page, pageSize);
    }

    private static DateOnly? RequiredDate(string? text, string field, string missing, FieldErrors errors)
    {
        if (Text(text) is null)
        {
            errors.Add(field, missing);
            return null;
        }

        return OptionalDate(text, field, errors);
    }

    private static DateOnly? OptionalDate(string? text, string field, FieldErrors errors)
    {
        if (Text(text) is not { } trimmed)
        {
            return null;
        }

        if (ChangeHistoryCodes.TryParseDate(trimmed, out var date))
        {
            return date;
        }

        errors.Add(field, "Podaj datę w formacie RRRR-MM-DD.");
        return null;
    }

    private static string? Text(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
