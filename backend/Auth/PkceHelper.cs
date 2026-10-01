using System.Security.Cryptography;
using System.Text;

namespace JobTracker.Api.Auth;

public static class PkceHelper
{
    public static string GenerateCodeVerifier() => GenerateRandomToken();

    public static string ComputeCodeChallenge(string codeVerifier)
    {
       var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
       return Base64UrlEncode(hash);
    }

    public static string GenerateRandomToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }
    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
