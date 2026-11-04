using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using StoneFlow.Access.Infrastructure.Options;

namespace StoneFlow.Access.Tests.Infrastructure;

public sealed class OptionsValidatorTests
{
    [Fact]
    public void Owner_email_is_required_and_message_names_the_key_without_value()
    {
        var missing = new OwnerOptionsValidator().Validate(null, new OwnerOptions());
        var invalid = new OwnerOptionsValidator().Validate(null, new OwnerOptions { Email = "nie-adres" });

        Assert.True(missing.Failed);
        Assert.Contains(OwnerOptions.EmailKey, missing.FailureMessage, StringComparison.Ordinal);
        Assert.True(invalid.Failed);
        Assert.DoesNotContain("nie-adres", invalid.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://app.example.com", true)]
    [InlineData("http://localhost:4200", true)]
    [InlineData("ftp://app.example.com", false)]
    [InlineData("https://app.example.com/?a=1", false)]
    [InlineData("https://app.example.com/#x", false)]
    [InlineData("/wzgledny", false)]
    public void App_base_url_must_be_absolute_http_without_query_or_fragment(string url, bool valid) =>
        Assert.Equal(valid, new AppOptionsValidator().Validate(null, new AppOptions { BaseUrl = url }).Succeeded);

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    public void Log_mode_only_in_development(string environment, bool valid) =>
        Assert.Equal(valid, new EmailOptionsValidator(new Environment(environment)).Validate(null, new EmailOptions { Mode = EmailMode.Log }).Succeeded);

    [Fact]
    public void Smtp_requires_host_and_sender_and_credentials_together()
    {
        var options = new EmailOptions
        {
            Mode = EmailMode.Smtp,
            Smtp = new SmtpOptions { Port = 0, Password = "tajne-haslo" },
        };

        var result = new EmailOptionsValidator(new Environment("Production")).Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Smtp:Host", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Smtp:Port", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Smtp:FromAddress", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Smtp:UserName", result.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("tajne-haslo", result.FailureMessage, StringComparison.Ordinal);
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "StoneFlow.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
