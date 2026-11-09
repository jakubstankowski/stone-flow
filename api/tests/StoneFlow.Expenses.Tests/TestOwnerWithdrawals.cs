using StoneFlow.Expenses.Application;
using StoneFlow.Expenses.Domain.OwnerWithdrawals;

namespace StoneFlow.Expenses.Tests;

/// <summary>Fikcyjne dane wypłat do testów (osoby wymyślone).</summary>
internal static class TestOwnerWithdrawals
{
    public static readonly DateTime CreatedAt = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    public static readonly DateOnly Today = new(2026, 11, 5);

    public static OwnerWithdrawalContent Content() => new("Właściciel A", 2000.00m, new DateOnly(2026, 11, 4), OwnerWithdrawalMethod.Transfer);

    public static OwnerWithdrawal Create(OwnerWithdrawalContent? content = null) =>
        OwnerWithdrawal.Create(Guid.NewGuid(), content ?? Content(), Guid.NewGuid(), new byte[32], CreatedAt);

    public static OwnerWithdrawalInput Input(
        string? person = "Właściciel A",
        string? amount = "2000.00",
        string? withdrawnOn = "2026-11-04",
        string? method = "transfer") =>
        new(person, amount, withdrawnOn, method);
}
