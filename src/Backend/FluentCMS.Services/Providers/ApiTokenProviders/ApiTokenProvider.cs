using System.Security.Cryptography;
using System.Text;

namespace FluentCMS.Providers;

public class JwtApiTokenProvider : IApiTokenProvider
{
    // TODO: This should be stored securely, and read from appsettings.json maybe?
    private static readonly string _secretKey = "your-secret-key"; 

    public string GenerateKey()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    public string GenerateSecret(string apiKey)
    {
        // generate a secret key from the api key
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToBase64String(hash);
    }
}
