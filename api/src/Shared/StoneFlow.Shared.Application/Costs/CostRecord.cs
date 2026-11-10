namespace StoneFlow.Shared.Application.Costs;

/// <summary>
/// Wydatek jako koszt (SF-19, projekcja w SQL, bez encji): data kosztu, kwota brutto ze znakiem
/// (korekta od dostawcy jest ujemna) i kod kategorii (<c>materialPurchase</c>, …).
/// </summary>
public sealed record CostRecord(Guid Id, DateOnly CostDate, decimal GrossAmount, string Category);
