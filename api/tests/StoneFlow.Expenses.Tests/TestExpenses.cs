using StoneFlow.Expenses.Application;
using StoneFlow.Expenses.Domain.Expenses;

namespace StoneFlow.Expenses.Tests;

/// <summary>Fikcyjne dane wydatków do testów (bez danych identyfikujących sklep).</summary>
internal static class TestExpenses
{
    public static readonly DateTime CreatedAt = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    public static readonly DateOnly Today = new(2026, 11, 5);

    public static ExpenseContent Content() => new(
        "Cięcie płyt",
        ExpenseCategory.Cutting,
        "Dostawca A",
        new DateOnly(2026, 10, 28),
        DueDate: new DateOnly(2026, 11, 10),
        PaidOn: null,
        GrossAmount: 1234.50m,
        NetAmount: 1003.66m,
        DocumentReference: "FV 12/2026",
        PaymentMethod: ExpensePaymentMethod.Transfer,
        Payer: null,
        Comment: null);

    public static Expense Create(ExpenseContent? content = null) =>
        Expense.CreateManual(Guid.NewGuid(), content ?? Content(), Guid.NewGuid(), new byte[32], CreatedAt);

    public static ExpenseInput Input(
        string? description = "Cięcie płyt",
        string? category = "cutting",
        string? counterparty = "Dostawca A",
        string? costDate = "2026-10-28",
        string? dueDate = "2026-11-10",
        string? paidOn = null,
        string? grossAmount = "1234.50",
        string? netAmount = "1003.66",
        string? documentReference = "FV 12/2026",
        string? paymentMethod = "transfer",
        string? payer = null,
        string? comment = null) =>
        new(description, category, counterparty, costDate, dueDate, paidOn, grossAmount, netAmount, documentReference, paymentMethod, payer, comment);
}
