using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using StoneFlow.Access.Domain.PasswordReset;

namespace StoneFlow.Access.Application.PasswordReset;

/// <summary>Token resetu: 256 losowych bitów w Base64Url; baza przechowuje tylko SHA-256.</summary>
public static class ResetToken
{
    private const int TokenBytes = 32;

    /// <summary>Długość tekstu Base64Url dla 32 bajtów (bez dopełnienia).</summary>
    public const int EncodedLength = 43;

    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));

    public static byte[] Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return SHA256.HashData(Encoding.ASCII.GetBytes(token));
    }

    /// <summary>Hash tokenu z linku; <c>null</c> dla tekstu, który nie może być tokenem.</summary>
    public static byte[]? TryHash(string? token)
    {
        if (token is null || token.Length != EncodedLength || !Base64Url.IsValid(token, out var decodedLength) || decodedLength != TokenBytes)
        {
            return null;
        }

        var hash = Hash(token);
        return hash.Length == PasswordResetToken.HashLength ? hash : null;
    }
}
