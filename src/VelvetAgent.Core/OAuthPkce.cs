using System.Security.Cryptography;

namespace VelvetAgent.Core;

public sealed record OAuthPkceChallenge(string State, string CodeVerifier, string CodeChallenge)
{
    public static OAuthPkceChallenge Create()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var digest = SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier));
        return new(state, verifier, Base64Url(digest));
    }

    static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
