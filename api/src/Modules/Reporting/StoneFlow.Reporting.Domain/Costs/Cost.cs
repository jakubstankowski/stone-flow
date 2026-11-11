namespace StoneFlow.Reporting.Domain.Costs;

/// <summary>Wydatek jako fakt kosztu (SF-19): data kosztu, kwota brutto ze znakiem i kod kategorii.</summary>
public sealed record Cost(Guid Id, DateOnly CostDate, decimal GrossAmount, string Category);
