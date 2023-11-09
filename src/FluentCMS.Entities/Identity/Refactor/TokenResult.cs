namespace FluentCMS.Entities.Identity;

public class TokenResult
{
    public virtual string AccessToken { get; set; }
    public virtual string RefreshToken { get; set; }
    public virtual DateTime Expiry { get; set; }
}