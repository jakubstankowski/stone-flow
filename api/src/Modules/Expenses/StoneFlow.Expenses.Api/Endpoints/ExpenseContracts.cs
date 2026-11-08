using StoneFlow.Expenses.Application;
using StoneFlow.Expenses.Application.GetExpense;
using StoneFlow.Expenses.Application.ListExpenses;

namespace StoneFlow.Expenses.Api.Endpoints;

// Kontrakt HTTP: kwoty jako tekst dziesiętny z kropką i opcjonalnym minusem ("-1234.50"), daty
// yyyy-MM-dd, słowniki jako kody camelCase (ExpenseCodes), wersja jako base64 z rowversion.

/// <summary>Ciało <c>POST</c> i <c>PUT</c>; <see cref="Version"/> tylko w <c>PUT</c>.</summary>
public sealed record ExpenseRequest(
    string? Description,
    string? Category,
    string? Counterparty,
    string? CostDate,
    string? DueDate,
    string? PaidOn,
    string? GrossAmount,
    string? NetAmount,
    string? DocumentReference,
    string? PaymentMethod,
    string? Payer,
    string? Comment,
    string? Version = null);

/// <summary>Ciało <c>mark-paid</c>.</summary>
public sealed record MarkExpensePaidRequest(string? PaidOn, string? PaymentMethod, string? Version);

/// <summary>Ciało <c>unmark-paid</c> i <c>DELETE</c>.</summary>
public sealed record ExpenseVersionRequest(string? Version);

public sealed record ExpenseCreatedResponse(Guid Id, string Version);

public sealed record ExpenseVersionResponse(string Version);

public sealed record ExpenseListItemResponse(
    Guid Id,
    string Description,
    string Category,
    string? Counterparty,
    string CostDate,
    string? DueDate,
    string? PaidOn,
    string GrossAmount,
    string PaymentStatus,
    bool Overdue);

public sealed record ExpenseListPageResponse(IReadOnlyList<ExpenseListItemResponse> Items, int Page, int PageSize, int Total);

public sealed record ExpenseDetailsResponse(
    Guid Id,
    string Description,
    string Category,
    string? Counterparty,
    string CostDate,
    string? DueDate,
    string? PaidOn,
    string GrossAmount,
    string? NetAmount,
    string? DocumentReference,
    string? PaymentMethod,
    string? Payer,
    string? Comment,
    string PaymentStatus,
    bool Overdue,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string Version);

internal static class ExpenseContracts
{
    public static ExpenseInput ToInput(ExpenseRequest? request) => request is null
        ? new ExpenseInput(null, null, null, null, null, null, null, null, null, null, null, null)
        : new ExpenseInput(
            request.Description,
            request.Category,
            request.Counterparty,
            request.CostDate,
            request.DueDate,
            request.PaidOn,
            request.GrossAmount,
            request.NetAmount,
            request.DocumentReference,
            request.PaymentMethod,
            request.Payer,
            request.Comment);

    public static ExpenseListPageResponse ToResponse(ExpenseListPageDto page) =>
        new([.. page.Items.Select(ToResponse)], page.Page, page.PageSize, page.Total);

    public static ExpenseDetailsResponse ToResponse(ExpenseDetailsDto details)
    {
        var e = details.Expense;
        return new ExpenseDetailsResponse(
            e.Id,
            e.Description,
            ExpenseCodes.Of(e.Category),
            e.Counterparty,
            ExpenseInput.FormatDate(e.CostDate),
            Date(e.DueDate),
            Date(e.PaidOn),
            ExpenseInput.FormatAmount(e.GrossAmount),
            e.NetAmount is { } net ? ExpenseInput.FormatAmount(net) : null,
            e.DocumentReference,
            e.PaymentMethod is { } method ? ExpenseCodes.Of(method) : null,
            e.Payer,
            e.Comment,
            ExpenseCodes.Of(details.Payment.Status),
            details.Payment.Overdue,
            e.CreatedAtUtc,
            e.UpdatedAtUtc,
            ExpenseVersion.Format(e.Version));
    }

    private static ExpenseListItemResponse ToResponse(ExpenseListItemDto item)
    {
        var row = item.Row;
        return new ExpenseListItemResponse(
            row.Id,
            row.Description,
            ExpenseCodes.Of(row.Category),
            row.Counterparty,
            ExpenseInput.FormatDate(row.CostDate),
            Date(row.DueDate),
            Date(row.PaidOn),
            ExpenseInput.FormatAmount(row.GrossAmount),
            ExpenseCodes.Of(item.Payment.Status),
            item.Payment.Overdue);
    }

    private static string? Date(DateOnly? date) => date is { } value ? ExpenseInput.FormatDate(value) : null;
}
