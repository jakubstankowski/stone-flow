using StoneFlow.Access.Application.Abstractions;

namespace StoneFlow.Access.Application.PasswordReset;

/// <summary>Link i treść wiadomości z linkiem ustawienia hasła.</summary>
public static class PasswordResetEmail
{
    public const string Subject = "Stone Flow — ustawienie hasła";

    /// <summary>
    /// <c>{BaseUrl}/reset-hasla/nowe#konto={id}&amp;token={token}</c>: token jest we fragmencie,
    /// więc nie trafia do serwera hostingu, logów ani nagłówka Referer. Link nie zawiera e-maila.
    /// </summary>
    public static Uri Link(Uri appBaseUrl, Guid accountId, string token)
    {
        ArgumentNullException.ThrowIfNull(appBaseUrl);
        ArgumentException.ThrowIfNullOrEmpty(token);

        var baseUrl = appBaseUrl.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return new Uri($"{baseUrl}/reset-hasla/nowe#konto={accountId:D}&token={Uri.EscapeDataString(token)}");
    }

    public static EmailMessage Create(string to, Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);

        var body =
            "Dzień dobry,\n\n" +
            "otrzymaliśmy prośbę o ustawienie hasła do Stone Flow. Aby ustawić nowe hasło, otwórz link:\n\n" +
            $"{link.AbsoluteUri}\n\n" +
            "Link jest ważny 30 minut i działa tylko raz.\n" +
            "Jeśli ta prośba nie pochodzi od Ciebie, zignoruj tę wiadomość — hasło pozostanie bez zmian.\n";

        return new EmailMessage(to, Subject, body);
    }
}
