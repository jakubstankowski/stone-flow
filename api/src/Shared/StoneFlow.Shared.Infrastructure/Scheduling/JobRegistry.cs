using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Shared.Infrastructure.Scheduling;

/// <summary>Definicje zarejestrowanych zadań, w kolejności klucza. Nie korzysta z bazy.</summary>
public sealed class JobRegistry
{
    public JobRegistry(IEnumerable<JobDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var list = definitions.ToList();
        var duplicates = list.GroupBy(d => d.Key, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException($"Zadania harmonogramu mają powtórzone klucze: {string.Join(", ", duplicates)}.");
        }

        Definitions = [.. list.OrderBy(d => d.Key, StringComparer.Ordinal)];
    }

    public IReadOnlyList<JobDefinition> Definitions { get; }

    public JobDefinition? Find(string key) => Definitions.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));
}
