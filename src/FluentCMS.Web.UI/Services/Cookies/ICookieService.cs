namespace FluentCMS.Web.UI.Services.Cookies;

public interface ICookieService
{
    public Task<IEnumerable<Cookie>> GetAllAsync();
    public Task<Cookie?> GetAsync(string key);
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    public Task SetAsync(string key, string value, DateTimeOffset? expiration, CancellationToken cancellationToken = default);
    public Task SetAsync(Cookie cookie, CancellationToken cancellationToken = default);
}
