using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Argus.Server.Infrastructure;

/// <summary>Random bearer secrets (agent keys, enrollment tokens) and their at-rest hashes.</summary>
public static class SecretTokens
{
    private const int SecretBytes = 32;

    /// <summary>Length of the random part: 32 bytes as unpadded base64url.</summary>
    private const int EncodedLength = 43;

    /// <summary>Creates a 256-bit random secret with a recognisable prefix, e.g. <c>argus_ak_…</c>.</summary>
    public static string Generate(string prefix) =>
        prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretBytes));

    /// <summary>
    /// SHA-256 of the token as lowercase hex. The tokens carry 256 bits of entropy, so a fast hash is
    /// safe and lets the hash double as the lookup key.
    /// </summary>
    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Cheap shape check to reject junk before touching the database.</summary>
    public static bool LooksValid(string? token, string prefix) =>
        token is not null
        && token.Length == prefix.Length + EncodedLength
        && token.StartsWith(prefix, StringComparison.Ordinal);

    /// <summary>A short, non-secret excerpt of a token for display, e.g. <c>argus_et_Ab12…</c>.</summary>
    public static string DisplayPrefix(string token, string prefix) => token[..(prefix.Length + 4)] + "…";
}
