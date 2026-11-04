using System.Text.Json;
using StoneFlow.Api.IntegrationTests.Infrastructure;

namespace StoneFlow.Api.IntegrationTests.Access;

/// <summary>Konfiguracja wdrożenia modułu Access: brak lub błąd blokuje start (komunikat bez wartości).</summary>
public sealed class AccessStartupTests
{
    [Fact]
    public async Task Missing_owner_email_fails_startup()
    {
        await using var factory = new StoneFlowApiFactory().Without("StoneFlow:Owner:Email");

        StartupFailure.AssertOptionsValidation(factory, "StoneFlow:Owner:Email");
    }

    [Fact]
    public async Task Invalid_owner_email_fails_startup_without_value_in_message()
    {
        await using var factory = new StoneFlowApiFactory().With("StoneFlow:Owner:Email", "nie-adres");

        StartupFailure.AssertOptionsValidation(factory, "StoneFlow:Owner:Email", forbiddenValue: "nie-adres");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/wzgledny")]
    [InlineData("https://app.example.com/?x=1")]
    public async Task Invalid_app_base_url_fails_startup(string? value)
    {
        await using var factory = new StoneFlowApiFactory().With("StoneFlow:App:BaseUrl", value);

        StartupFailure.AssertOptionsValidation(factory, "StoneFlow:App:BaseUrl");
    }

    [Fact]
    public async Task Smtp_without_host_fails_startup()
    {
        await using var factory = new StoneFlowApiFactory().Without("StoneFlow:Email:Smtp:Host");

        StartupFailure.AssertOptionsValidation(factory, "StoneFlow:Email:Smtp:Host");
    }

    [Fact]
    public async Task Smtp_password_without_user_name_fails_startup_without_value_in_message()
    {
        await using var factory = new StoneFlowApiFactory().With("StoneFlow:Email:Smtp:Password", "tajne-haslo-smtp");

        StartupFailure.AssertOptionsValidation(factory, "StoneFlow:Email:Smtp:UserName", forbiddenValue: "tajne-haslo-smtp");
    }

    [Fact]
    public async Task Log_mode_outside_development_fails_startup()
    {
        await using var factory = new StoneFlowApiFactory().With("StoneFlow:Email:Mode", "Log");

        StartupFailure.AssertOptionsValidation(factory, "StoneFlow:Email:Mode");
    }

    [Fact]
    public async Task Log_mode_is_allowed_in_development()
    {
        await using var factory = new StoneFlowApiFactory { Environment = "Development" }.With("StoneFlow:Email:Mode", "Log");

        var exception = Record.Exception(() => factory.CreateClient().Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public void Base_appsettings_contain_no_owner_email_or_email_configuration()
    {
        var path = Path.Combine(RepositoryRoot.Path, "api", "src", "StoneFlow.Api", "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        Assert.False(document.RootElement.TryGetProperty("StoneFlow", out _), "Konfiguracja właściciela i SMTP należy do wdrożenia, nie do appsettings.json.");
    }
}
