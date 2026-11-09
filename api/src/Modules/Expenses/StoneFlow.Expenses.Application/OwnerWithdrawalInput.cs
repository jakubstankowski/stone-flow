using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Domain.OwnerWithdrawals;

namespace StoneFlow.Expenses.Application;

/// <summary>
/// Dane wypłaty z formularza (tekst jak w JSON). Normalizacja jak w <see cref="ExpenseInput"/>:
/// osoba przycięta, kwota w kulturze niezmiennej z kropką, najwyżej dwa miejsca, bez zaokrąglania;
/// data <c>yyyy-MM-dd</c>. Regułę kwoty (&gt; 0 zł) wyznacza domena; „dzisiaj” podaje wywołujący
/// (Europe/Warsaw).
/// </summary>
public sealed record OwnerWithdrawalInput(string? Person, string? Amount, string? WithdrawnOn, string? Method)
{
    private static readonly JsonSerializerOptions HashJson = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>Walidacja i normalizacja treści wypłaty (dodanie i poprawa).</summary>
    public OwnerWithdrawalContent? Validate(FieldErrors errors, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var before = errors.IsEmpty;

        var person = ExpenseInput.Text(Person);
        if (person is null)
        {
            errors.Add(OwnerWithdrawalFieldKeys.Person, OwnerWithdrawalMessages.PersonRequired);
        }
        else if (person.Length > OwnerWithdrawal.PersonMaxLength)
        {
            errors.Add(OwnerWithdrawalFieldKeys.Person, OwnerWithdrawalMessages.PersonTooLong);
        }

        decimal? amount = null;
        var amountError = ExpenseInput.ParseAmount(Amount, out var parsedAmount);
        if (amountError != ExpenseInput.AmountError.None)
        {
            errors.Add(OwnerWithdrawalFieldKeys.Amount, AmountMessage(amountError));
        }
        else if (OwnerWithdrawal.AmountErrorOf(parsedAmount) is var rule && rule != OwnerWithdrawalAmountError.None)
        {
            errors.Add(
                OwnerWithdrawalFieldKeys.Amount,
                rule == OwnerWithdrawalAmountError.NotPositive ? OwnerWithdrawalMessages.AmountNotPositive : OwnerWithdrawalMessages.AmountTooLarge);
        }
        else
        {
            amount = parsedAmount;
        }

        DateOnly? withdrawnOn = null;
        if (ExpenseInput.Text(WithdrawnOn) is not { } dateText)
        {
            errors.Add(OwnerWithdrawalFieldKeys.WithdrawnOn, OwnerWithdrawalMessages.WithdrawnOnRequired);
        }
        else if (!ExpenseInput.TryParseDate(dateText, out var parsedDate))
        {
            errors.Add(OwnerWithdrawalFieldKeys.WithdrawnOn, OwnerWithdrawalMessages.WithdrawnOnInvalid);
        }
        else if (parsedDate > today)
        {
            errors.Add(OwnerWithdrawalFieldKeys.WithdrawnOn, OwnerWithdrawalMessages.WithdrawnOnInFuture);
        }
        else
        {
            withdrawnOn = parsedDate;
        }

        OwnerWithdrawalMethod? method = null;
        if (ExpenseInput.Text(Method) is not { } methodCode)
        {
            errors.Add(OwnerWithdrawalFieldKeys.Method, OwnerWithdrawalMessages.MethodRequired);
        }
        else if (OwnerWithdrawalCodes.TryParse(methodCode, out var parsedMethod))
        {
            method = parsedMethod;
        }
        else
        {
            errors.Add(OwnerWithdrawalFieldKeys.Method, OwnerWithdrawalMessages.MethodUnknown);
        }

        return before && errors.IsEmpty && person is not null && amount is { } a && withdrawnOn is { } d && method is { } m
            ? new OwnerWithdrawalContent(person, a, d, m)
            : null;
    }

    /// <summary>SHA-256 kanonicznego JSON-a znormalizowanej treści (32 bajty).</summary>
    public static byte[] ComputeRequestHash(OwnerWithdrawalContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var model = new HashModel(
            content.Person,
            ExpenseInput.FormatAmount(content.Amount),
            ExpenseInput.FormatDate(content.WithdrawnOn),
            OwnerWithdrawalCodes.Of(content.Method));
        return SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(model, HashJson));
    }

    private static string AmountMessage(ExpenseInput.AmountError error) => error switch
    {
        ExpenseInput.AmountError.Missing => OwnerWithdrawalMessages.AmountRequired,
        ExpenseInput.AmountError.Invalid => OwnerWithdrawalMessages.AmountInvalid,
        ExpenseInput.AmountError.Scale => OwnerWithdrawalMessages.AmountScale,
        _ => OwnerWithdrawalMessages.AmountTooLarge,
    };

    private sealed record HashModel(string Person, string Amount, string WithdrawnOn, string Method);
}

/// <summary>Wersja wypłaty na granicy API: <c>rowversion</c> zakodowany base64 (format jak <see cref="ExpenseVersion"/>).</summary>
public static class OwnerWithdrawalVersion
{
    /// <summary>Waliduje wersję z żądania; błąd pod kluczem <c>version</c>.</summary>
    public static byte[]? Validate(string? text, FieldErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var version = ExpenseVersion.Parse(text);
        if (version is null)
        {
            errors.Add(
                OwnerWithdrawalFieldKeys.Version,
                string.IsNullOrWhiteSpace(text) ? OwnerWithdrawalMessages.VersionRequired : OwnerWithdrawalMessages.VersionInvalid);
        }

        return version;
    }
}

/// <summary>Klucze błędów walidacji wypłaty: nazwy pól JSON żądania.</summary>
public static class OwnerWithdrawalFieldKeys
{
    public const string Person = "person";
    public const string Amount = "amount";
    public const string WithdrawnOn = "withdrawnOn";
    public const string Method = "method";
    public const string Version = "version";
}

/// <summary>Komunikaty walidacji pól wypłaty (UI po polsku).</summary>
public static class OwnerWithdrawalMessages
{
    public const string PersonRequired = "Podaj osobę.";
    public const string PersonTooLong = "Osoba może mieć najwyżej 100 znaków.";
    public const string AmountRequired = "Podaj kwotę.";
    public const string AmountInvalid = "Podaj kwotę jako liczbę, np. 1234.50.";
    public const string AmountScale = "Kwota może mieć najwyżej dwa miejsca po przecinku.";
    public const string AmountTooLarge = "Kwota jest zbyt duża.";
    public const string AmountNotPositive = "Kwota musi być większa od 0 zł.";
    public const string WithdrawnOnRequired = "Podaj datę wypłaty.";
    public const string WithdrawnOnInvalid = "Podaj poprawną datę wypłaty.";
    public const string WithdrawnOnInFuture = "Data wypłaty nie może być późniejsza niż dzisiaj.";
    public const string MethodRequired = "Wybierz metodę.";
    public const string MethodUnknown = "Nieznana metoda.";
    public const string VersionRequired = "Brak wersji wypłaty. Wczytaj dane ponownie.";
    public const string VersionInvalid = "Niepoprawna wersja wypłaty. Wczytaj dane ponownie.";
}
