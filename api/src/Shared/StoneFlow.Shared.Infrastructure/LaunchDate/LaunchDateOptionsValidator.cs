using System.Globalization;
using Microsoft.Extensions.Options;

namespace StoneFlow.Shared.Infrastructure.LaunchDate;

public sealed class LaunchDateOptionsValidator : IValidateOptions<LaunchDateOptions>
{
    public ValidateOptionsResult Validate(string? name, LaunchDateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.LaunchDate))
        {
            return ValidateOptionsResult.Fail(
                $"Brak wymaganej daty uruchomienia '{LaunchDateOptions.LaunchDateKey}' w konfiguracji wdrożenia.");
        }

        return TryParse(options.LaunchDate, out _)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"Data uruchomienia '{LaunchDateOptions.LaunchDateKey}' musi mieć format {LaunchDateOptions.Format}.");
    }

    public static bool TryParse(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(
            value,
            LaunchDateOptions.Format,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
}
