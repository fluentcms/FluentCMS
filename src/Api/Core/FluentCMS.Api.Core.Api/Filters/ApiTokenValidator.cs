using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;

namespace FluentCMS.Api.Core.Api.Filters;

internal interface IApiTokenValidator
{
    string GenerateKey();
    string GenerateSecret(string apiKey);
    bool Validate(string apiKey, string secretKey);
}

internal class ApiTokenValidator(IOptions<JwtApiTokenConfig> options) : IApiTokenValidator
{
    private readonly JwtApiTokenConfig _config = options.Value;

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

public class JwtApiTokenConfig
{
    public string Secret { get; set; } = default!;
}



