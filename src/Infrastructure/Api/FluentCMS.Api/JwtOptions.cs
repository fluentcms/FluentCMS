namespace FluentCMS.Api;

public class JwtOptions
{
    public string Issuer { get; set; } = default!;
    public string Audience { get; set; } = default!;
    public double TokenExpiry { get; set; } // second, -1 means never expire
    public double RefreshTokenExpiry { get; set; } // second, -1 means never expire
    public string Secret { get; set; } = default!;
}