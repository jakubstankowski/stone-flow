using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace StoneFlow.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Klient przeglądarkowy w testach: ciasteczka (sesja, antiforgery) obsługuje HttpClient fabryki,
/// a token z ciasteczka <c>XSRF-TOKEN</c> jest przepisywany do nagłówka jak w Angularze.
/// </summary>
public sealed class AuthClient(HttpClient client) : IDisposable
{
    public const string Password = "dobre-haslo-testowe-1";

    public HttpClient Http { get; } = client;

    public string? XsrfToken { get; private set; }

    /// <summary>Ostatnia wartość ciasteczka sesji ustawiona przez API (do testu skopiowanego ciasteczka).</summary>
    public string? SessionCookie { get; private set; }

    public static AuthClient Create(StoneFlowApiFactory factory) => new(factory.CreateClient());

    public async Task<HttpResponseMessage> GetCsrfAsync()
    {
        var response = await Http.GetAsync(new Uri("/api/auth/csrf", UriKind.Relative), TestContext.Current.CancellationToken);
        Remember(response);
        return response;
    }

    public async Task<HttpResponseMessage> PostAsync(string path, object? body = null, bool withCsrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        if (withCsrf && XsrfToken is not null)
        {
            request.Headers.Add("X-XSRF-TOKEN", XsrfToken);
        }

        var response = await Http.SendAsync(request, TestContext.Current.CancellationToken);
        Remember(response);
        return response;
    }

    public async Task<HttpResponseMessage> LoginAsync(string email = StoneFlowApiFactory.OwnerEmail, string password = Password)
    {
        (await GetCsrfAsync()).Dispose();
        return await PostAsync("/api/auth/login", new { email, password });
    }

    public async Task<HttpResponseMessage> RequestResetAsync(string email = StoneFlowApiFactory.OwnerEmail)
    {
        (await GetCsrfAsync()).Dispose();
        return await PostAsync("/api/auth/password-reset/request", new { email });
    }

    public async Task<HttpResponseMessage> ConfirmResetAsync(string accountId, string token, string newPassword = Password)
    {
        (await GetCsrfAsync()).Dispose();
        return await PostAsync("/api/auth/password-reset/confirm", new { accountId, token, newPassword });
    }

    public Task<HttpResponseMessage> GetSessionAsync() =>
        Http.GetAsync(new Uri("/api/auth/session", UriKind.Relative), TestContext.Current.CancellationToken);

    /// <summary>Tworzy konto właściciela procesem resetu i ustawia hasło <see cref="Password"/>.</summary>
    public async Task SetUpOwnerAsync(StoneFlowApiFactory factory)
    {
        using var request = await RequestResetAsync();
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var link = factory.Emails.LastLink();
        using var confirm = await ConfirmResetAsync(link.AccountId, link.Token);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
    }

    public static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    public void Dispose() => Http.Dispose();

    private void Remember(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return;
        }

        foreach (var cookie in cookies)
        {
            var nameValue = cookie.Split(';', 2)[0];
            var separator = nameValue.IndexOf('=', StringComparison.Ordinal);
            var name = nameValue[..separator];
            var value = nameValue[(separator + 1)..];
            if (name == "XSRF-TOKEN")
            {
                XsrfToken = Uri.UnescapeDataString(value);
            }
            else if (name.EndsWith("sf_session", StringComparison.Ordinal) && value.Length > 0)
            {
                SessionCookie = nameValue;
            }
        }
    }
}
