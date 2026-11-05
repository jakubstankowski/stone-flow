namespace StoneFlow.Orders.Application.Abstractions;

/// <summary>
/// Błędy pól: ścieżka JSON pola (<c>value</c>, <c>customer.name</c>,
/// <c>operational.suppliers[0]</c>) → komunikaty. Frontend przypina błąd do pola po tym kluczu.
/// </summary>
public sealed class FieldErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool IsEmpty => _errors.Count == 0;

    public FieldErrors Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var messages))
        {
            messages = [];
            _errors[field] = messages;
        }

        messages.Add(message);
        return this;
    }

    public bool Contains(string field) => _errors.ContainsKey(field);

    public IDictionary<string, string[]> ToDictionary() =>
        _errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
}
