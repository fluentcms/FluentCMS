using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;

namespace FluentCMS.Providers;

public class DefaultApiTokenProvider : IApiTokenProvider
{
    // TODO: This should be stored securely, and read from appsettings.json maybe?
    private static readonly string _secretKey = "your-secret-key";

    public string GenerateKey()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    public string GenerateSecret(string apiKey)
    {
        var signingKey = Encoding.ASCII.GetBytes(apiKey + "." + _secretKey);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(signingKey), SecurityAlgorithms.HmacSha256Signature)
        };

        var tokenHandler = new JwtSecurityTokenHandler();

        return tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));
    }

    public bool Validate(string apiKey, string secretKey)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var signingKey = Encoding.ASCII.GetBytes(apiKey + "." + _secretKey);
            tokenHandler.ValidateToken(secretKey, new TokenValidationParameters
            {
                IssuerSigningKey = new SymmetricSecurityKey(signingKey),
                ValidateIssuer = false,
                ValidateAudience = false,
            }, out SecurityToken validatedToken);

            return true;
        }
        catch (Exception)
        {
            return false;
        }

    }
}
