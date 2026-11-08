using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Domain.Expenses;
using StoneFlow.Expenses.Domain.Recurring;

namespace StoneFlow.Expenses.Application.Recurring;

/// <summary>Zwalidowana i znormalizowana definicja z formularza.</summary>
public sealed record RecurringDefinitionInput(RecurringContent Content, RecurringScheduleInput Schedule);

/// <summary>
/// Dane definicji cyklicznej z formularza (tekst jak w JSON; dzień miesiąca jako liczba). Walidacja
/// pól bez stanu (format, zakresy, kwoty, koniec ≥ początek); reguły terminów sprawdza domena na stanie
/// definicji. Teksty i kwoty parsują pomocniki <see cref="ExpenseInput"/> (bez kopii).
/// </summary>
public sealed record RecurringExpenseInput(
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
    string? EndsOn)
{
    private static readonly JsonSerializerOptions HashJson = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>Walidacja i normalizacja treści i harmonogramu (dodanie i zmiana).</summary>
    public RecurringDefinitionInput? Validate(FieldErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var before = errors.IsEmpty;

        var description = ExpenseInput.RequiredDescription(Description, errors);
        var category = ExpenseInput.RequiredCategory(Category, errors);
        var counterparty = ExpenseInput.Limited(
            Counterparty, Expense.CounterpartyMaxLength, ExpenseFieldKeys.Counterparty, ExpenseMessages.CounterpartyTooLong, errors);
        var amounts = ValidateAmounts(errors);
        var method = ExpenseInput.OptionalMethod(PaymentMethod, errors);
        var payer = ExpenseInput.Limited(Payer, Expense.PayerMaxLength, ExpenseFieldKeys.Payer, ExpenseMessages.PayerTooLong, errors);
        var comment = ExpenseInput.Limited(Comment, Expense.CommentMaxLength, ExpenseFieldKeys.Comment, ExpenseMessages.CommentTooLong, errors);
        var schedule = ValidateSchedule(DayOfMonth, StartsOn, EndsOn, errors);

        return before && errors.IsEmpty && description is not null && category is { } c && amounts is { } a && schedule is not null
            ? new RecurringDefinitionInput(new RecurringContent(description, c, counterparty, a.Gross, a.Net, method, payer, comment), schedule)
            : null;
    }

    /// <summary>Walidacja harmonogramu bez stanu (dodanie, zmiana i podgląd).</summary>
    public static RecurringScheduleInput? ValidateSchedule(int? dayOfMonth, string? startsOnText, string? endsOnText, FieldErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var before = errors.IsEmpty;

        if (dayOfMonth is not { } day || !RecurringRules.IsValidDay(day))
        {
            errors.Add(RecurringFieldKeys.DayOfMonth, RecurringMessages.DayOfMonthInvalid);
        }

        DateOnly? startsOn = null;
        if (ExpenseInput.Text(startsOnText) is not { } startText)
        {
            errors.Add(RecurringFieldKeys.StartsOn, RecurringMessages.StartsOnRequired);
        }
        else if (!ExpenseInput.TryParseDate(startText, out var parsedStart))
        {
            errors.Add(RecurringFieldKeys.StartsOn, RecurringMessages.StartsOnInvalid);
        }
        else if (!RecurringRules.IsInDateRange(parsedStart))
        {
            errors.Add(RecurringFieldKeys.StartsOn, RecurringMessages.StartsOnOutOfRange);
        }
        else
        {
            startsOn = parsedStart;
        }

        var endsOn = ExpenseInput.OptionalDate(endsOnText, RecurringFieldKeys.EndsOn, RecurringMessages.EndsOnInvalid, errors);
        if (endsOn > RecurringRules.MaxDate)
        {
            errors.Add(RecurringFieldKeys.EndsOn, RecurringMessages.EndsOnOutOfRange);
        }
        else if (endsOn < startsOn)
        {
            errors.Add(RecurringFieldKeys.EndsOn, RecurringMessages.EndsBeforeStart);
        }

        return before && errors.IsEmpty && dayOfMonth is { } validDay && startsOn is { } start
            ? new RecurringScheduleInput(validDay, start, endsOn)
            : null;
    }

    /// <summary>SHA-256 kanonicznego JSON-a znormalizowanej definicji (32 bajty), jak w SF-15.</summary>
    public static byte[] ComputeRequestHash(RecurringDefinitionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var content = input.Content;
        var schedule = input.Schedule;
        var model = new HashModel(
            content.Description,
            ExpenseCodes.Of(content.Category),
            content.Counterparty,
            ExpenseInput.FormatAmount(content.GrossAmount),
            content.NetAmount is { } net ? ExpenseInput.FormatAmount(net) : null,
            content.PaymentMethod is { } method ? ExpenseCodes.Of(method) : null,
            content.Payer,
            content.Comment,
            schedule.DayOfMonth.ToString(CultureInfo.InvariantCulture),
            ExpenseInput.FormatDate(schedule.StartsOn),
            schedule.EndsOn is { } endsOn ? ExpenseInput.FormatDate(endsOn) : null);
        return SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(model, HashJson));
    }

    private Amounts? ValidateAmounts(FieldErrors errors)
    {
        decimal? gross = null;
        var grossError = ExpenseInput.ParseAmount(GrossAmount, out var parsedGross);
        if (grossError != ExpenseInput.AmountError.None)
        {
            errors.Add(ExpenseFieldKeys.GrossAmount, ExpenseInput.AmountMessage(grossError, ExpenseMessages.GrossRequired));
        }
        else if (RecurringRules.CheckAmounts(parsedGross, null) is var grossRule && grossRule != RecurringAmountError.None)
        {
            errors.Add(ExpenseFieldKeys.GrossAmount, RuleMessage(grossRule));
        }
        else
        {
            gross = parsedGross;
        }

        decimal? net = null;
        if (ExpenseInput.Text(NetAmount) is not null)
        {
            var netError = ExpenseInput.ParseAmount(NetAmount, out var parsedNet);
            if (netError != ExpenseInput.AmountError.None)
            {
                errors.Add(ExpenseFieldKeys.NetAmount, ExpenseInput.AmountMessage(netError, ExpenseMessages.AmountInvalid));
                return null;
            }

            // Porównanie z brutto tylko przy poprawnym brutto; ujemne netto jest błędem zawsze.
            var netRule = gross is { } validGross
                ? RecurringRules.CheckAmounts(validGross, parsedNet)
                : parsedNet < 0 ? RecurringAmountError.NetNegative : RecurringAmountError.None;
            if (netRule != RecurringAmountError.None)
            {
                errors.Add(ExpenseFieldKeys.NetAmount, RuleMessage(netRule));
                return null;
            }

            net = parsedNet;
        }

        return gross is { } g ? new Amounts(g, net) : null;
    }

    private static string RuleMessage(RecurringAmountError error) => error switch
    {
        RecurringAmountError.GrossNotPositive => RecurringMessages.GrossNotPositive,
        RecurringAmountError.NetNegative => RecurringMessages.NetNegative,
        RecurringAmountError.NetExceedsGross => ExpenseMessages.NetExceedsGross,
        _ => ExpenseMessages.AmountTooLarge,
    };

    private sealed record Amounts(decimal Gross, decimal? Net);

    private sealed record HashModel(
        string Description,
        string Category,
        string? Counterparty,
        string GrossAmount,
        string? NetAmount,
        string? PaymentMethod,
        string? Payer,
        string? Comment,
        string DayOfMonth,
        string StartsOn,
        string? EndsOn);
}

/// <summary>Klucze błędów harmonogramu: nazwy pól JSON żądania (pozostałe jak w <see cref="ExpenseFieldKeys"/>).</summary>
public static class RecurringFieldKeys
{
    public const string DayOfMonth = "dayOfMonth";
    public const string StartsOn = "startsOn";
    public const string EndsOn = "endsOn";
    public const string DefinitionId = "definitionId";
}

/// <summary>Komunikaty walidacji definicji cyklicznej (UI po polsku).</summary>
public static class RecurringMessages
{
    public const string GrossNotPositive = "Kwota wydatku cyklicznego musi być większa od 0 zł.";
    public const string NetNegative = "Kwota netto nie może być ujemna.";
    public const string DayOfMonthInvalid = "Dzień miesiąca musi być liczbą od 1 do 31.";
    public const string StartsOnRequired = "Podaj początek.";
    public const string StartsOnInvalid = "Podaj poprawną datę początku.";
    public const string StartsOnOutOfRange = "Początek musi przypadać w latach 2000–2099.";
    public const string EndsOnInvalid = "Podaj poprawną datę końca.";
    public const string EndsOnOutOfRange = "Koniec musi przypadać najpóźniej 31.12.2099.";
    public const string EndsBeforeStart = "Koniec nie może być wcześniejszy niż początek.";
    public const string NoDueInRange = "W tym okresie nie wypada żaden termin.";
    public const string DefinitionIdInvalid = "Niepoprawny identyfikator wydatku cyklicznego.";
}
