namespace FluentCMS.Plugins.IdentityManager.Options;

public class TokenValidationParametersOptions
{
    public bool ValidateIssuer { get; set; } = true;
    public bool ValidateAudience { get; set; } = true;
    public bool ValidateLifetime { get; set; } = true;
    public bool ValidateIssuerSigningKey { get; set; } = true;
    public bool RequireExpirationTime { get; set; } = true;
    public bool RequireSignedTokens { get; set; } = true;
    public TimeSpan ClockSkew { get; set; } = TimeSpan.Zero;
}