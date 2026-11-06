using System.Security.Cryptography;
using System.Text;

namespace StoneFlow.Integrations.Application.WooCommerce;

/// <summary>
/// Podpis webhooka WooCommerce: nagłówek <c>X-WC-Webhook-Signature</c> = Base64(HMAC-SHA256(sekret,
/// surowe bajty treści)). Porównanie w czasie stałym.
/// </summary>
public static class WebhookSignature
{
    public static bool IsValid(ReadOnlySpan<byte> body, string? header, string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        if (string.IsNullOrWhiteSpace(header))
        {
            return false;
        }

        Span<byte> received = stackalloc byte[64];
        if (!Convert.TryFromBase64String(header.Trim(), received, out var length) || length != HMACSHA256.HashSizeInBytes)
        {
            return false;
        }

        Span<byte> expected = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body, expected);
        return CryptographicOperations.FixedTimeEquals(expected, received[..length]);
    }

    /// <summary>Podpis treści (testy, narzędzia).</summary>
    public static string Sign(ReadOnlySpan<byte> body, string secret) =>
        Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));
}
