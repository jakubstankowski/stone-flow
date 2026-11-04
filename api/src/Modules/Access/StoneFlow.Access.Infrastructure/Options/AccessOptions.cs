using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StoneFlow.Access.Domain.Accounts;

namespace StoneFlow.Access.Infrastructure.Options;

/// <summary>Sekcja <c>StoneFlow:Owner</c>.</summary>
public sealed class OwnerOptions
{
    public const string SectionName = "StoneFlow:Owner";
    public const string EmailKey = SectionName + ":Email";

    public string? Email { get; set; }
}

/// <summary>Sekcja <c>StoneFlow:App</c>.</summary>
public sealed class AppOptions
{
    public const string SectionName = "StoneFlow:App";
    public const string BaseUrlKey = SectionName + ":BaseUrl";

    /// <summary>Adres aplikacji frontendowej używany w linkach (np. <c>https://app.example.com</c>).</summary>
    public string? BaseUrl { get; set; }
}

public enum EmailMode
{
    Smtp,

    /// <summary>Treść wiadomości trafia do logu — tylko w środowisku Development.</summary>
    Log,
}

/// <summary>Sekcja <c>StoneFlow:Email</c>.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "StoneFlow:Email";
    public const string ModeKey = SectionName + ":Mode";
    public const string SmtpKey = SectionName + ":Smtp";

    public EmailMode? Mode { get; set; }

    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class SmtpOptions
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    /// <summary><c>true</c>: STARTTLS (wymagany); <c>false</c>: TLS od początku połączenia. Bez szyfrowania nigdy.</summary>
    public bool UseStartTls { get; set; } = true;

    public string? UserName { get; set; }

    /// <summary>Tylko w user-secrets lub zmiennych środowiskowych, nigdy w repozytorium.</summary>
    public string? Password { get; set; }

    public string? FromAddress { get; set; }

    public string FromName { get; set; } = "Stone Flow";
}

/// <summary>Komunikaty wskazują klucz konfiguracji, nigdy wartość.</summary>
public sealed class OwnerOptionsValidator : IValidateOptions<OwnerOptions>
{
    public ValidateOptionsResult Validate(string? name, OwnerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Email))
        {
            return ValidateOptionsResult.Fail($"Brak wymaganego adresu właściciela '{OwnerOptions.EmailKey}' w konfiguracji wdrożenia.");
        }

        return EmailAddress.IsValid(options.Email)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"Adres właściciela '{OwnerOptions.EmailKey}' nie jest poprawnym adresem e-mail.");
    }
}

public sealed class AppOptionsValidator : IValidateOptions<AppOptions>
{
    public ValidateOptionsResult Validate(string? name, AppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return ValidateOptionsResult.Fail($"Brak wymaganego adresu aplikacji '{AppOptions.BaseUrlKey}' w konfiguracji wdrożenia.");
        }

        return TryParse(options.BaseUrl, out _)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"Adres aplikacji '{AppOptions.BaseUrlKey}' musi być bezwzględnym adresem http(s) bez zapytania i fragmentu.");
    }

    public static bool TryParse(string? value, out Uri uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri!)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment);
}

public sealed class EmailOptionsValidator(IHostEnvironment environment) : IValidateOptions<EmailOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        switch (options.Mode)
        {
            case null:
                return ValidateOptionsResult.Fail($"Brak wymaganego trybu wysyłki '{EmailOptions.ModeKey}' (Smtp albo Log).");
            case EmailMode.Log:
                return environment.IsDevelopment()
                    ? ValidateOptionsResult.Success
                    : ValidateOptionsResult.Fail($"Tryb '{EmailOptions.ModeKey}=Log' jest dozwolony tylko w środowisku Development.");
        }

        var smtp = options.Smtp;
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(smtp.Host))
        {
            failures.Add($"Brak wymaganego serwera '{EmailOptions.SmtpKey}:Host'.");
        }

        if (smtp.Port is < 1 or > 65535)
        {
            failures.Add($"Port '{EmailOptions.SmtpKey}:Port' musi mieścić się w zakresie 1–65535.");
        }

        if (!EmailAddress.IsValid(smtp.FromAddress))
        {
            failures.Add($"Adres nadawcy '{EmailOptions.SmtpKey}:FromAddress' musi być poprawnym adresem e-mail.");
        }

        if (string.IsNullOrEmpty(smtp.UserName) != string.IsNullOrEmpty(smtp.Password))
        {
            failures.Add($"'{EmailOptions.SmtpKey}:UserName' i '{EmailOptions.SmtpKey}:Password' muszą być podane razem albo wcale.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
