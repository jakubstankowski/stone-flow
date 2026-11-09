using StoneFlow.Reporting.Application.Operations;

namespace StoneFlow.Reporting.Api.Endpoints;

// Kontrakt HTTP: kwoty jako tekst dziesiętny z kropką i opcjonalnym minusem ("-1234.50"), daty
// yyyy-MM-dd, rodzaje, kierunki i metody jako kody. Kwota to kwota zapisana — kierunek niesie
// wyłącznie pole direction.

public sealed record OperationItemResponse(
    string Kind,
    Guid Id,
    string Date,
    string Direction,
    string Amount,
    string? Method,
    string? RefundKind,
    string? Description,
    string? Category,
    string SourceType,
    Guid SourceId,
    string SourceLabel);

public sealed record OperationTotalsResponse(string Inflows, string Outflows, string Change);

public sealed record OperationsPageResponse(
    IReadOnlyList<OperationItemResponse> Items,
    int Page,
    int PageSize,
    int Total,
    OperationTotalsResponse Totals,
    string EffectiveFrom,
    string LaunchDate);

internal static class OperationContracts
{
    public static OperationsPageResponse ToResponse(OperationsPageDto page) => new(
        [.. page.Items.Select(ToResponse)],
        page.Page,
        page.PageSize,
        page.Total,
        new OperationTotalsResponse(
            OperationCodes.FormatAmount(page.Totals.Inflows),
            OperationCodes.FormatAmount(page.Totals.Outflows),
            OperationCodes.FormatAmount(page.Totals.Change)),
        OperationCodes.FormatDate(page.EffectiveFrom),
        OperationCodes.FormatDate(page.LaunchDate));

    private static OperationItemResponse ToResponse(OperationItemDto item)
    {
        var operation = item.Operation;
        return new OperationItemResponse(
            OperationCodes.Of(operation.Kind),
            operation.Id,
            OperationCodes.FormatDate(operation.Date),
            OperationCodes.Of(item.Direction),
            OperationCodes.FormatAmount(operation.Amount),
            operation.Method,
            operation.RefundKind,
            operation.Description,
            operation.Category,
            OperationCodes.SourceTypeOf(operation.Kind),
            operation.SourceId,
            operation.SourceLabel);
    }
}
