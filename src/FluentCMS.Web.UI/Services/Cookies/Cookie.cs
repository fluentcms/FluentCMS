namespace FluentCMS.Web.UI.Services.Cookies;

public class Cookie(string key, string value, DateTimeOffset? expiration = null)
{
    public string Key { get; set; } = key;
    public string Value { get; set; } = value;
    public DateTimeOffset? Expiration { get; set; } = expiration;
}
