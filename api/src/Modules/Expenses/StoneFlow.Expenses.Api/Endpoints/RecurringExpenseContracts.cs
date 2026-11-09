using StoneFlow.Expenses.Application;
using StoneFlow.Expenses.Application.GetRecurringExpense;
using StoneFlow.Expenses.Application.ListRecurringExpenses;
using StoneFlow.Expenses.Application.PreviewRecurringExpense;
using StoneFlow.Expenses.Application.Recurring;

namespace StoneFlow.Expenses.Api.Endpoints;

// Kontrakt HTTP: kwoty jako tekst dziesiętny z kropką, daty yyyy-MM-dd, okres yyyy-MM, dzień miesiąca
// jako liczba, słowniki jako kody camelCase, stan active/paused/ended, wersja jako base64.

/// <summary>Ciało <c>POST</c> i <c>PUT</c>; <see cref="Version"/> tylko w <c>PUT</c>.</summary>
public sealed record RecurringExpenseRequest(
    string? Description,
    string? Category,
    string? Counterparty,
    string? GrossAmount,
    string? NetAmount,
    string? PaymentMethod,
    string? Payer,
    string? Comment,
    int? DayOfMonth,
    string? StartsOn,
    string? EndsOn,
    string? Version = null);

/// <summary>Ciało <c>pause</c>, <c>resume</c> i <c>DELETE</c>.</summary>
public sealed record RecurringExpenseVersionRequest(string? Version);

public sealed record RecurringExpenseCreatedResponse(Guid Id, string Version, int CreatedOccurrences);

public sealed record RecurringExpenseChangedResponse(string Version, int CreatedOccurrences);

public sealed record RecurringExpenseListItemResponse(
    Guid Id,
    string Description,
    string Category,
    string? Counterparty,
    string GrossAmount,
    int DayOfMonth,
    string StartsOn,
    string? EndsOn,
    string Status,
    string? PausedOn,
    string? NextDueDate);

public sealed record RecurringExpenseListPageResponse(IReadOnlyList<RecurringExpenseListItemResponse> Items, int Page, int PageSize, int Total);

public sealed record RecurringExpenseResumePreviewResponse(int OccurrencesToCreate, string? NextDueDate);

public sealed record RecurringExpenseDetailsResponse(
    Guid Id,
    string Description,
    string Category,
    string? Counterparty,
    string GrossAmount,
    string? NetAmount,
    string? PaymentMethod,
    string? Payer,
    string? Comment,
    int DayOfMonth,
    string StartsOn,
    string? EndsOn,
    string Status,
    string? PausedOn,
    string? NextDueDate,
    int OccurrenceCount,
    RecurringExpenseResumePreviewResponse? ResumePreview,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string Version);

public sealed record RecurringExpensePreviewResponse(
    int OccurrencesToCreate,
    string? FirstImmediateDueDate,
    int BeforeLaunchCount,
    string? NextDueDate,
    bool Paused,
    int DroppedDueCount,
    int CreatedAfterEndCount,
    int CreatedBeforeStartCount);

internal static class RecurringExpenseContracts
{
    public static RecurringExpenseInput ToInput(RecurringExpenseRequest? request) => request is null
        ? new RecurringExpenseInput(null, null, null, null, null, null, null, null, null, null, null)
        : new RecurringExpenseInput(
            request.Description,
            request.Category,
            request.Counterparty,
            request.GrossAmount,
            request.NetAmount,
            request.PaymentMethod,
            request.Payer,
            request.Comment,
            request.DayOfMonth,
            request.StartsOn,
            request.EndsOn);

    public static RecurringExpenseListPageResponse ToResponse(RecurringListPageDto page) =>
        new([.. page.Items.Select(ToResponse)], page.Page, page.PageSize, page.Total);

    public static RecurringExpenseDetailsResponse ToResponse(RecurringExpenseDetailsDto details)
    {
        var d = details.Definition;
        return new RecurringExpenseDetailsResponse(
            d.Id,
            d.Description,
            ExpenseCodes.Of(d.Category),
            d.Counterparty,
            ExpenseInput.FormatAmount(d.GrossAmount),
            d.NetAmount is { } net ? ExpenseInput.FormatAmount(net) : null,
            d.PaymentMethod is { } method ? ExpenseCodes.Of(method) : null,
            d.Payer,
            d.Comment,
            d.DayOfMonth,
            ExpenseInput.FormatDate(d.StartsOn),
            Date(d.EndsOn),
            RecurringCodes.Of(details.Status),
            Date(d.PausedOn),
            Date(details.NextDueDate),
            d.OccurrenceCount,
            details.ResumePreview is { } resume
                ? new RecurringExpenseResumePreviewResponse(resume.OccurrencesToCreate, Date(resume.NextDueDate))
                : null,
            d.CreatedAtUtc,
            d.UpdatedAtUtc,
            ExpenseVersion.Format(d.Version));
    }

    public static RecurringExpensePreviewResponse ToResponse(RecurringExpensePreviewDto preview) =>
        new(
            preview.OccurrencesToCreate,
            Date(preview.FirstImmediateDueDate),
            preview.BeforeLaunchCount,
            Date(preview.NextDueDate),
            preview.Paused,
            preview.DroppedDueCount,
            preview.CreatedAfterEndCount,
            preview.CreatedBeforeStartCount);

    private static RecurringExpenseListItemResponse ToResponse(RecurringListItemDto item)
    {
        var row = item.Row;
        return new RecurringExpenseListItemResponse(
            row.Id,
            row.Description,
            ExpenseCodes.Of(row.Category),
            row.Counterparty,
            ExpenseInput.FormatAmount(row.GrossAmount),
            row.DayOfMonth,
            ExpenseInput.FormatDate(row.StartsOn),
            Date(row.EndsOn),
            RecurringCodes.Of(item.Status),
            Date(row.PausedOn),
            Date(item.NextDueDate));
    }

    private static string? Date(DateOnly? date) => date is { } value ? ExpenseInput.FormatDate(value) : null;
}
