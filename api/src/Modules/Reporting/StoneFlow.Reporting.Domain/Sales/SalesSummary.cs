using StoneFlow.Reporting.Domain.Periods;
using StoneFlow.Shared.Kernel.Sales;

namespace StoneFlow.Reporting.Domain.Sales;

/// <summary>Sprzedaż kanału.</summary>
public sealed record ChannelAmount(string Channel, decimal Amount);

/// <summary>
/// Sprzedaż okresu (SF-19): suma kwot ze znakiem składników wchodzących do sprzedaży, liczba
/// zamówień, podział na kanały (pełny słownik, suma kanałów = sprzedaż) i wartości w przedziałach
/// wykresu (suma przedziałów = sprzedaż). Sumy zapisanych kwot bez zaokrąglania.
/// </summary>
public sealed record SalesSummary(decimal Sales, int OrderCount, IReadOnlyList<ChannelAmount> Channels, IReadOnlyList<decimal> BucketSales)
{
    /// <exception cref="ArgumentException">Składnik z kanałem spoza <paramref name="channels"/> (błąd programu).</exception>
    public static SalesSummary Of(IEnumerable<SalesComponent> components, ReportPeriod period, IReadOnlyList<string> channels)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(channels);

        var byChannel = channels.ToDictionary(channel => channel, _ => 0m, StringComparer.Ordinal);
        var buckets = new decimal[period.Buckets().Count];
        var sales = 0m;
        var orderCount = 0;

        if (!period.BeforeLaunch)
        {
            foreach (var component in components.Where(c => SalesPolicy.Includes(c, period.EffectiveFrom, period.Range.To, period.LaunchDate)))
            {
                if (!byChannel.TryGetValue(component.Channel, out var channelSales))
                {
                    throw new ArgumentException($"Kanał „{component.Channel}” spoza słownika kanałów.", nameof(components));
                }

                var amount = SalesPolicy.SignedAmount(component);
                sales += amount;
                byChannel[component.Channel] = channelSales + amount;
                buckets[period.BucketIndexOf(component.Date)] += amount;
                if (component.Kind == SalesComponentKind.Order)
                {
                    orderCount++;
                }
            }
        }

        return new SalesSummary(
            sales,
            orderCount,
            [.. channels.Select(channel => new ChannelAmount(channel, byChannel[channel]))],
            buckets);
    }
}
