namespace StoneFlow.Access.Application;

/// <summary>Komunikaty walidacji pól (UI po polsku) i nazwy pól w odpowiedziach.</summary>
public static class AccessMessages
{
    public const string EmailField = "email";
    public const string PasswordField = "password";
    public const string NewPasswordField = "newPassword";

    public const string EmailRequired = "Podaj adres e-mail.";
    public const string EmailInvalid = "Podaj poprawny adres e-mail.";
    public const string PasswordRequired = "Podaj hasło.";
    public const string NewPasswordRequired = "Podaj nowe hasło.";
    public const string NewPasswordTooShort = "Hasło musi mieć co najmniej 12 znaków.";
    public const string NewPasswordSameAsEmail = "Hasło nie może być takie samo jak adres e-mail.";
    public const string NewPasswordRejected = "Nie udało się ustawić tego hasła. Wybierz inne.";
}

/// <summary>Błędy pól: nazwa pola → komunikaty.</summary>
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

    public IDictionary<string, string[]> ToDictionary() =>
        _errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
}
