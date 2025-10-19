namespace FluentCMS.Api.Plugins.IdentityManagement;

public class JwtToken
{
    public Guid Id { get; set; }           // Token ID (jti claim)
    public Guid UserId { get; set; }       // User who owns the token
    public DateTime IssuedAt { get; set; } // When token was issued
    public DateTime ExpiresAt { get; set; } // When token expires
    public string DeviceInfo { get; set; } = string.Empty; // Device/browser info
    public string IpAddress { get; set; } = string.Empty;  // IP address at login
    public bool IsRevoked { get; set; }    // Manual revocation flag
}
