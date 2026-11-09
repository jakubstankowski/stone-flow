using StoneFlow.Expenses.Application.Abstractions;

namespace StoneFlow.Expenses.Application;

public enum OwnerWithdrawalChangeOutcome
{
    /// <summary>Zapisano zmiany albo nic się nie zmieniło (wersja bez zmian); po usunięciu bez wersji.</summary>
    Saved,
    ValidationFailed,
    NotFound,

    /// <summary>Wersja klienta jest nieaktualna albo wiersz zmieniono w międzyczasie.</summary>
    Conflict,
}

/// <summary>Wynik poprawy albo usunięcia wypłaty; <see cref="Version"/> po zapisie.</summary>
public sealed record OwnerWithdrawalChangeResult(OwnerWithdrawalChangeOutcome Outcome, byte[]? Version = null, FieldErrors? Errors = null)
{
    public static OwnerWithdrawalChangeResult Invalid(FieldErrors errors) => new(OwnerWithdrawalChangeOutcome.ValidationFailed, Errors: errors);
}
