namespace FluentCMS.Services.Models;

public class UserToken
{
    public required string AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime Expiry { get; set; }
}
