using Microsoft.Extensions.Options;
using StoneFlow.Shared.Application.LaunchDate;

namespace StoneFlow.Shared.Infrastructure.LaunchDate;

internal sealed class ConfiguredLaunchDate(IOptions<LaunchDateOptions> options) : ILaunchDate
{
    private readonly Lazy<DateOnly> _value = new(() =>
        LaunchDateOptionsValidator.TryParse(options.Value.LaunchDate, out var date)
            ? date
            : throw new InvalidOperationException(
                $"Nieprawidłowa data uruchomienia '{LaunchDateOptions.LaunchDateKey}'."));

    public DateOnly Value => _value.Value;
}
