using System.Globalization;
using StoneFlow.Reporting.Application.Abstractions;
using StoneFlow.Reporting.Application.ChangeHistory;

namespace StoneFlow.Reporting.Api.Endpoints;

// Kontrakt HTTP „Historii zmian”: identyfikator wpisu jako tekst, czas ISO 8601 UTC z „Z”, daty
// yyyy-MM-dd, rodzaje, operacje i autorzy jako kody. Wartości pól w formacie zapisu audytu, bez
// zmian i w kolejności z wpisu. Identyfikator konta autora nie jest zwracany.

public sealed record ChangeHistoryPageResponse(IReadOnlyList<ChangeHistoryItemResponse> Items, int Total, int Page, int PageSize);

public sealed record ChangeHistoryItemResponse(
    string Id,
    string OccurredAt,
    ChangeHistoryAuthorResponse Author,
    ChangeHistoryObjectResponse Object,
    string Operation,
    IReadOnlyList<ChangeHistoryChangeResponse> Changes);

public sealed record ChangeHistoryAuthorResponse(string Kind);

public sealed record ChangeHistoryObjectResponse(
    string Type,
    Guid Id,
    string BusinessDate,
    string Label,
    ChangeHistoryParentResponse? Parent,
    bool Deleted);

public sealed record ChangeHistoryParentResponse(string Type, Guid Id);

public sealed record ChangeHistoryChangeResponse(string Field, string? OldValue, string? NewValue, bool IsPersonalData);

internal static class ChangeHistoryContracts
{
    public static ChangeHistoryPageResponse ToResponse(ChangeHistoryPageDto page) =>
        new([.. page.Items.Select(ToResponse)], page.Total, page.Page, page.PageSize);

    private static ChangeHistoryItemResponse ToResponse(ChangeHistoryEntryDto entry) => new(
        entry.Id.ToString(CultureInfo.InvariantCulture),
        ChangeHistoryCodes.FormatTimestamp(entry.OccurredAtUtc),
        new ChangeHistoryAuthorResponse(ChangeHistoryCodes.Of(entry.AuthorKind)),
        new ChangeHistoryObjectResponse(
            ChangeHistoryCodes.Of(entry.ObjectType),
            entry.ObjectId,
            ChangeHistoryCodes.FormatDate(entry.BusinessDate),
            entry.Label,
            entry is { ParentType: { } parentType, ParentId: { } parentId }
                ? new ChangeHistoryParentResponse(ChangeHistoryCodes.Of(parentType), parentId)
                : null,
            entry.Deleted),
        ChangeHistoryCodes.Of(entry.Operation),
        [.. entry.Changes.Select(change => new ChangeHistoryChangeResponse(change.Field, change.OldValue, change.NewValue, change.IsPersonalData))]);
}
