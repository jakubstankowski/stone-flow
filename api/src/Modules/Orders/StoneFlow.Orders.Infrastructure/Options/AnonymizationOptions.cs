using System.Globalization;
using Microsoft.Extensions.Options;
using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Orders;

namespace StoneFlow.Orders.Infrastructure.Options;

/// <summary>
/// Konfiguracja anonimizacji danych klienta (SF-22), sekcja <c>StoneFlow:Anonymization</c>. Okres retencji
/// (D-09) jest wiązany jako tekst, żeby brak klucza, pusta wartość i same białe znaki oznaczały funkcję
/// wyłączoną, a każda inna wartość spoza liczb całkowitych 1–1200 zatrzymywała start z komunikatem
/// walidacji (wiązanie <c>int?</c> rzuciłoby wyjątek bindera bez nazwy klucza).
/// </summary>
public sealed class AnonymizationOptions
{
    public const string SectionName = "StoneFlow:Anonymization";

    public const string RetentionMonthsKey = SectionName + ":RetentionMonths";

    /// <summary>Liczba miesięcy retencji jako tekst; brak = funkcja wyłączona. Bez wartości domyślnej.</summary>
    public string? RetentionMonths { get; set; }

    /// <summary>
    /// <c>true</c> i <paramref name="months"/> = <c>null</c> dla braku wartości (także pustej); <c>true</c> i
    /// liczba dla liczby całkowitej 1–1200; <c>false</c> dla każdej innej wartości.
    /// </summary>
    public static bool TryParseRetentionMonths(string? text, out int? months)
    {
        months = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            && value is >= CustomerAnonymization.MinRetentionMonths and <= CustomerAnonymization.MaxRetentionMonths)
        {
            months = value;
            return true;
        }

        return false;
    }
}

/// <summary>Walidacja przy starcie; komunikat podaje tylko klucz i zakres, bez wartości z konfiguracji.</summary>
public sealed class AnonymizationOptionsValidator : IValidateOptions<AnonymizationOptions>
{
    public ValidateOptionsResult Validate(string? name, AnonymizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return AnonymizationOptions.TryParseRetentionMonths(options.RetentionMonths, out _)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"'{AnonymizationOptions.RetentionMonthsKey}' musi być liczbą całkowitą miesięcy od 1 do 1200 albo pozostać nieustawione (funkcja wyłączona).");
    }
}

/// <summary>Okres retencji z opcji (wartość stała od startu aplikacji).</summary>
internal sealed class ConfiguredAnonymizationSettings(IOptions<AnonymizationOptions> options) : IAnonymizationSettings
{
    public int? RetentionMonths =>
        AnonymizationOptions.TryParseRetentionMonths(options.Value.RetentionMonths, out var months) ? months : null;
}
