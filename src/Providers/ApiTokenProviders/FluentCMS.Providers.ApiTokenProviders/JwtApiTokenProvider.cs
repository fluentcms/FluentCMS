using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;

namespace FluentCMS.Providers.ApiTokenProviders;

public class JwtApiTokenProvider : IApiTokenProvider
{
    private readonly JwtApiTokenConfig _config;

    public JwtApiTokenProvider(IOptions<JwtApiTokenConfig> options)
    {
        _config = options.Value;
    }

    public string GenerateKey()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    public string GenerateSecret(string apiKey)
    {
        var signingKey = Encoding.ASCII.GetBytes(apiKey + "." + _config.Secret);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Expires = DateTime.Now.AddYears(10),
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
            var signingKey = Encoding.ASCII.GetBytes(apiKey + "." + _config.Secret);
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
