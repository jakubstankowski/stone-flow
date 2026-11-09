using StoneFlow.Expenses.Application;
using StoneFlow.Expenses.Application.GetOwnerWithdrawal;

namespace StoneFlow.Expenses.Api.Endpoints;

// Kontrakt HTTP: kwota jako tekst dziesiętny z kropką ("2000.00"), data yyyy-MM-dd, metoda jako kod
// (transfer, blik, cash), wersja jako base64 z rowversion.

/// <summary>Ciało <c>POST</c> i <c>PUT</c>; <see cref="Version"/> tylko w <c>PUT</c>.</summary>
public sealed record OwnerWithdrawalRequest(string? Person, string? Amount, string? WithdrawnOn, string? Method, string? Version = null);

/// <summary>Ciało <c>DELETE</c>.</summary>
public sealed record OwnerWithdrawalVersionRequest(string? Version);

public sealed record OwnerWithdrawalCreatedResponse(Guid Id, string Version);

public sealed record OwnerWithdrawalVersionResponse(string Version);

public sealed record OwnerWithdrawalPeopleResponse(IReadOnlyList<string> People);

public sealed record OwnerWithdrawalDetailsResponse(
    Guid Id,
    string Person,
    string Amount,
    string WithdrawnOn,
    string Method,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string Version);

internal static class OwnerWithdrawalContracts
{
    public static OwnerWithdrawalInput ToInput(OwnerWithdrawalRequest? request) => request is null
        ? new OwnerWithdrawalInput(null, null, null, null)
        : new OwnerWithdrawalInput(request.Person, request.Amount, request.WithdrawnOn, request.Method);

    public static OwnerWithdrawalDetailsResponse ToResponse(OwnerWithdrawalRecordDto record) => new(
        record.Id,
        record.Person,
        ExpenseInput.FormatAmount(record.Amount),
        ExpenseInput.FormatDate(record.WithdrawnOn),
        OwnerWithdrawalCodes.Of(record.Method),
        record.CreatedAtUtc,
        record.UpdatedAtUtc,
        ExpenseVersion.Format(record.Version));
}
