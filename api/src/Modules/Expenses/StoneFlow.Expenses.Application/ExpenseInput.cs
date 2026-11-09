using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Domain.Expenses;

namespace StoneFlow.Expenses.Application;

/// <summary>Data zapłaty i metoda z akcji „Oznacz jako opłacony” (po walidacji).</summary>
public sealed record ExpensePaidInput(DateOnly PaidOn, ExpensePaymentMethod? PaymentMethod);

/// <summary>
/// Dane wydatku z formularza (tekst jak w JSON). Normalizacja: teksty przycięte, pusty tekst to
/// <c>null</c>; kwoty w kulturze niezmiennej z kropką i opcjonalnym minusem, najwyżej dwa miejsca
/// po przecinku, bez zaokrąglania; daty <c>yyyy-MM-dd</c>. Reguły kwot należą do domeny
/// (<see cref="ExpenseAmounts"/>); „dzisiaj” dla daty zapłaty podaje wywołujący (Europe/Warsaw).
/// </summary>
public sealed partial record ExpenseInput(
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
    string? Comment)
{
    private static readonly JsonSerializerOptions HashJson = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>Walidacja i normalizacja treści wydatku (dodanie i poprawa).</summary>
    public ExpenseContent? Validate(FieldErrors errors, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var before = errors.IsEmpty;

        var description = RequiredDescription(Description, errors);

        var category = RequiredCategory(Category, errors);
        var counterparty = Limited(Counterparty, Expense.CounterpartyMaxLength, ExpenseFieldKeys.Counterparty, ExpenseMessages.CounterpartyTooLong, errors);

        DateOnly? costDate = null;
        if (Text(CostDate) is not { } costText)
        {
            errors.Add(ExpenseFieldKeys.CostDate, ExpenseMessages.CostDateRequired);
        }
        else if (TryParseDate(costText, out var parsedCost))
        {
            costDate = parsedCost;
        }
        else
        {
            errors.Add(ExpenseFieldKeys.CostDate, ExpenseMessages.CostDateInvalid);
        }

        var dueDate = OptionalDate(DueDate, ExpenseFieldKeys.DueDate, ExpenseMessages.DueDateInvalid, errors);
        var paidOn = OptionalDate(PaidOn, ExpenseFieldKeys.PaidOn, ExpenseMessages.PaidOnInvalid, errors);
        if (paidOn > today)
        {
            errors.Add(ExpenseFieldKeys.PaidOn, ExpenseMessages.PaidOnInFuture);
        }

        decimal? gross = null;
        var grossError = ParseAmount(GrossAmount, out var parsedGross);
        if (grossError != AmountError.None)
        {
            errors.Add(ExpenseFieldKeys.GrossAmount, AmountMessage(grossError, ExpenseMessages.GrossRequired));
        }
        else if (ExpenseAmounts.GrossErrorOf(parsedGross) is var grossRule && grossRule != ExpenseAmountError.None)
        {
            errors.Add(ExpenseFieldKeys.GrossAmount, RuleMessage(grossRule));
        }
        else
        {
            gross = parsedGross;
        }

        decimal? net = null;
        if (Text(NetAmount) is not null)
        {
            var netError = ParseAmount(NetAmount, out var parsedNet);
            if (netError != AmountError.None)
            {
                errors.Add(ExpenseFieldKeys.NetAmount, AmountMessage(netError, ExpenseMessages.AmountInvalid));
            }
            else if (gross is { } validGross && ExpenseAmounts.NetErrorOf(validGross, parsedNet) is var netRule && netRule != ExpenseAmountError.None)
            {
                errors.Add(ExpenseFieldKeys.NetAmount, RuleMessage(netRule));
            }
            else
            {
                net = parsedNet;
            }
        }

        var documentReference = Limited(
            DocumentReference, Expense.DocumentReferenceMaxLength, ExpenseFieldKeys.DocumentReference, ExpenseMessages.DocumentReferenceTooLong, errors);
        var method = OptionalMethod(PaymentMethod, errors);
        var payer = Limited(Payer, Expense.PayerMaxLength, ExpenseFieldKeys.Payer, ExpenseMessages.PayerTooLong, errors);
        var comment = Limited(Comment, Expense.CommentMaxLength, ExpenseFieldKeys.Comment, ExpenseMessages.CommentTooLong, errors);

        return before && errors.IsEmpty && description is not null && category is { } c && costDate is { } d && gross is { } g
            ? new ExpenseContent(description, c, counterparty, d, dueDate, paidOn, g, net, documentReference, method, payer, comment)
            : null;
    }

    /// <summary>Opis: wymagany, najwyżej 200 znaków (wspólne z definicją cykliczną).</summary>
    internal static string? RequiredDescription(string? text, FieldErrors errors)
    {
        var description = Text(text);
        if (description is null)
        {
            errors.Add(ExpenseFieldKeys.Description, ExpenseMessages.DescriptionRequired);
        }
        else if (description.Length > Expense.DescriptionMaxLength)
        {
            errors.Add(ExpenseFieldKeys.Description, ExpenseMessages.DescriptionTooLong);
        }

        return description;
    }

    /// <summary>Kategoria: wymagana, znany kod (wspólne z definicją cykliczną).</summary>
    internal static ExpenseCategory? RequiredCategory(string? text, FieldErrors errors)
    {
        if (Text(text) is not { } categoryCode)
        {
            errors.Add(ExpenseFieldKeys.Category, ExpenseMessages.CategoryRequired);
            return null;
        }

        if (ExpenseCodes.TryParse(categoryCode, out ExpenseCategory category))
        {
            return category;
        }

        errors.Add(ExpenseFieldKeys.Category, ExpenseMessages.CategoryUnknown);
        return null;
    }

    /// <summary>Walidacja akcji „Oznacz jako opłacony”: data wymagana i nie późniejsza niż dzisiaj, znana metoda albo brak.</summary>
    public static ExpensePaidInput? ValidatePaid(string? paidOnText, string? methodText, FieldErrors errors, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var before = errors.IsEmpty;

        DateOnly? paidOn = null;
        if (Text(paidOnText) is not { } text)
        {
            errors.Add(ExpenseFieldKeys.PaidOn, ExpenseMessages.PaidOnRequired);
        }
        else if (!TryParseDate(text, out var parsed))
        {
            errors.Add(ExpenseFieldKeys.PaidOn, ExpenseMessages.PaidOnInvalid);
        }
        else if (parsed > today)
        {
            errors.Add(ExpenseFieldKeys.PaidOn, ExpenseMessages.PaidOnInFuture);
        }
        else
        {
            paidOn = parsed;
        }

        var method = OptionalMethod(methodText, errors);
        return before && errors.IsEmpty && paidOn is { } date ? new ExpensePaidInput(date, method) : null;
    }

    /// <summary>SHA-256 kanonicznego JSON-a znormalizowanej treści (32 bajty).</summary>
    public static byte[] ComputeRequestHash(ExpenseContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var model = new HashModel(
            content.Description,
            ExpenseCodes.Of(content.Category),
            content.Counterparty,
            FormatDate(content.CostDate),
            content.DueDate is { } due ? FormatDate(due) : null,
            content.PaidOn is { } paid ? FormatDate(paid) : null,
            FormatAmount(content.GrossAmount),
            content.NetAmount is { } net ? FormatAmount(net) : null,
            content.DocumentReference,
            content.PaymentMethod is { } method ? ExpenseCodes.Of(method) : null,
            content.Payer,
            content.Comment);
        return SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(model, HashJson));
    }

    /// <summary>Kwota w kulturze niezmiennej z dwoma miejscami (<c>-1234.50</c>).</summary>
    public static string FormatAmount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Przycięty tekst albo <c>null</c> dla pustego.</summary>
    internal static string? Text(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    internal static bool TryParseDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>Tekst kwoty w kulturze niezmiennej (wzorzec, najwyżej dwa miejsca, zakres <c>decimal(18,2)</c>); bez reguł znaku.</summary>
    internal static AmountError ParseAmount(string? text, out decimal value)
    {
        value = 0;
        var trimmed = Text(text);
        if (trimmed is null)
        {
            return AmountError.Missing;
        }

        var match = AmountPattern().Match(trimmed);
        if (!match.Success)
        {
            return AmountError.Invalid;
        }

        if (match.Groups["fraction"].Value.Length > 2)
        {
            return AmountError.Scale;
        }

        if (!decimal.TryParse(trimmed, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            || !ExpenseAmounts.IsInRange(parsed))
        {
            return AmountError.TooLarge;
        }

        value = parsed;
        return AmountError.None;
    }

    internal static string AmountMessage(AmountError error, string missing) => error switch
    {
        AmountError.Missing => missing,
        AmountError.Invalid => ExpenseMessages.AmountInvalid,
        AmountError.Scale => ExpenseMessages.AmountScale,
        _ => ExpenseMessages.AmountTooLarge,
    };

    private static string RuleMessage(ExpenseAmountError error) => error switch
    {
        ExpenseAmountError.GrossZero => ExpenseMessages.GrossZero,
        ExpenseAmountError.NetSign => ExpenseMessages.NetSign,
        ExpenseAmountError.NetExceedsGross => ExpenseMessages.NetExceedsGross,
        _ => ExpenseMessages.AmountTooLarge,
    };

    internal static DateOnly? OptionalDate(string? text, string key, string invalid, FieldErrors errors)
    {
        if (Text(text) is not { } trimmed)
        {
            return null;
        }

        if (TryParseDate(trimmed, out var date))
        {
            return date;
        }

        errors.Add(key, invalid);
        return null;
    }

    internal static ExpensePaymentMethod? OptionalMethod(string? text, FieldErrors errors)
    {
        if (Text(text) is not { } code)
        {
            return null;
        }

        if (ExpenseCodes.TryParse(code, out ExpensePaymentMethod method))
        {
            return method;
        }

        errors.Add(ExpenseFieldKeys.PaymentMethod, ExpenseMessages.PaymentMethodUnknown);
        return null;
    }

    internal static string? Limited(string? text, int maxLength, string key, string message, FieldErrors errors)
    {
        var trimmed = Text(text);
        if (trimmed is { Length: var length } && length > maxLength)
        {
            errors.Add(key, message);
        }

        return trimmed;
    }

    [GeneratedRegex(@"^-?[0-9]+(\.(?<fraction>[0-9]+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex AmountPattern();

    internal enum AmountError
    {
        None,
        Missing,
        Invalid,
        Scale,
        TooLarge,
    }

    private sealed record HashModel(
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
        string? Comment);
}

/// <summary>Wersja wydatku na granicy API: <c>rowversion</c> zakodowany base64.</summary>
public static class ExpenseVersion
{
    private const int Length = 8;

    public static string Format(byte[] version) => Convert.ToBase64String(version);

    /// <summary>Bajty wersji albo <c>null</c>, gdy tekst nie jest wersją.</summary>
    public static byte[]? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var buffer = new byte[Length];
        return Convert.TryFromBase64String(text.Trim(), buffer, out var written) && written == Length ? buffer : null;
    }

    /// <summary>Waliduje wersję z żądania; błąd pod kluczem <c>version</c>.</summary>
    public static byte[]? Validate(string? text, FieldErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var version = Parse(text);
        if (version is null)
        {
            errors.Add(ExpenseFieldKeys.Version, string.IsNullOrWhiteSpace(text) ? ExpenseMessages.VersionRequired : ExpenseMessages.VersionInvalid);
        }

        return version;
    }
}

/// <summary>Klucze błędów walidacji: nazwy pól JSON żądania.</summary>
public static class ExpenseFieldKeys
{
    public const string Description = "description";
    public const string Category = "category";
    public const string Counterparty = "counterparty";
    public const string CostDate = "costDate";
    public const string DueDate = "dueDate";
    public const string PaidOn = "paidOn";
    public const string GrossAmount = "grossAmount";
    public const string NetAmount = "netAmount";
    public const string DocumentReference = "documentReference";
    public const string PaymentMethod = "paymentMethod";
    public const string Payer = "payer";
    public const string Comment = "comment";
    public const string Version = "version";
}

/// <summary>Komunikaty walidacji pól (UI po polsku).</summary>
public static class ExpenseMessages
{
    public const string DescriptionRequired = "Podaj opis wydatku.";
    public const string DescriptionTooLong = "Opis może mieć najwyżej 200 znaków.";
    public const string CategoryRequired = "Wybierz kategorię.";
    public const string CategoryUnknown = "Nieznana kategoria.";
    public const string CounterpartyTooLong = "Kontrahent może mieć najwyżej 200 znaków.";
    public const string CostDateRequired = "Podaj datę kosztu.";
    public const string CostDateInvalid = "Podaj poprawną datę kosztu.";
    public const string DueDateInvalid = "Podaj poprawny termin płatności.";
    public const string PaidOnRequired = "Podaj datę zapłaty.";
    public const string PaidOnInvalid = "Podaj poprawną datę zapłaty.";
    public const string PaidOnInFuture = "Data zapłaty nie może być późniejsza niż dzisiaj.";
    public const string GrossRequired = "Podaj kwotę brutto.";
    public const string AmountInvalid = "Podaj kwotę jako liczbę, np. 1234.50 albo -1234.50.";
    public const string AmountScale = "Kwota może mieć najwyżej dwa miejsca po przecinku.";
    public const string AmountTooLarge = "Kwota jest zbyt duża.";
    public const string GrossZero = "Kwota brutto nie może wynosić 0 zł.";
    public const string NetSign = "Kwota netto musi mieć ten sam znak co brutto.";
    public const string NetExceedsGross = "Kwota netto nie może być większa od brutto.";
    public const string DocumentReferenceTooLong = "Referencja dokumentu może mieć najwyżej 100 znaków.";
    public const string PaymentMethodUnknown = "Nieznana metoda płatności.";
    public const string PayerTooLong = "Płatnik może mieć najwyżej 100 znaków.";
    public const string CommentTooLong = "Komentarz może mieć najwyżej 2000 znaków.";
    public const string VersionRequired = "Brak wersji wydatku. Wczytaj dane ponownie.";
    public const string VersionInvalid = "Niepoprawna wersja wydatku. Wczytaj dane ponownie.";
}
