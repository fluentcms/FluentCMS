using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.UI.Services.Cookies;

internal class HttpContextCookieService : ICookieService
{
    private readonly HttpContext _httpContext;
    private readonly List<Cookie> _cache;

    public HttpContextCookieService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContext = httpContextAccessor.HttpContext!;
        _cache = _httpContext.Request.Cookies
            .Select(x => new Cookie(x.Key, x.Value)).ToList();
    }

    public Task<IEnumerable<Cookie>> GetAllAsync()
    {
        return Task.FromResult(_cache.ToList().AsEnumerable());
    }

    public Task<Cookie?> GetAsync(string key)
    {
        return Task.FromResult(_cache.FirstOrDefault(x => x.Key == key));
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var cookie = _cache.FirstOrDefault(x => x.Key == key);
        if (cookie is null) return Task.CompletedTask;

        _cache.Remove(cookie);
        _httpContext.Response.Cookies.Delete(key);

        return Task.CompletedTask;
    }

    public Task SetAsync(string key, string value, DateTimeOffset? expiration, CancellationToken cancellationToken = default)
    {
        _cache.Add(new Cookie(key, value, expiration));
        _httpContext.Response.Cookies.Append(key, value, new CookieOptions
        {
            Expires = expiration,
            Path = "/",
        });
        return Task.CompletedTask;
    }

    public Task SetAsync(Cookie cookie, CancellationToken cancellationToken = default)
        => SetAsync(cookie.Key, cookie.Value, cookie.Expiration, cancellationToken);
}
