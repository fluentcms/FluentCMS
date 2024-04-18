using FluentCMS.Shared;
using Microsoft.JSInterop;

namespace FluentCMS.Web.UI.Services;
public static class JsRuntimeCookieExtensions
{
    public static async Task SetCookieAsJsonAsync<T>(this IJSRuntime jsRuntime, string key, T obj, DateTimeOffset? expiration)
    {
        await jsRuntime.InvokeVoidAsync("eval", $"document.cookie = \"{key}={obj.SerializeToJson().UrlEncode()}; expires={expiration:R}; path=/\"");

    }
    public static async Task SetCookieAsJsonAsync<T>(this IJSRuntime jsRuntime, T obj, DateTimeOffset? expiration)
    {
        var key = typeof(T).Name;
        await jsRuntime.InvokeVoidAsync("eval", $"document.cookie = \"{key}={obj.SerializeToJson().UrlEncode()}; expires={expiration:R}; path=/\"");

    }
    public static async Task<Dictionary<string, string>> GetAllCookiesAsync(this IJSRuntime jsRuntime)
    {
        var raw = await jsRuntime.InvokeAsync<string>("eval", "document.cookie");
        if (string.IsNullOrEmpty(raw))
        {
            return [];
        }

        return raw.Split(";").Select(x => x.Trim()).Select(x =>
        {
            var split = x.Split("=");
            var key = split[0];
            var value = split[1];
            return new KeyValuePair<string, string>(key, value);
        }).ToDictionary();
    }
    public static async Task<T?> GetCookieAsync<T>(this IJSRuntime jsRuntime, string key)
    {
        return (await jsRuntime.GetAllCookiesAsync()).First(x => x.Key == key).Value.UrlDecode().DeserializeFromJson<T>();
    }

    public static async Task<T?> GetCookieAsync<T>(this IJSRuntime jsRuntime)
    {
        var key = typeof(T).Name;
        return (await jsRuntime.GetAllCookiesAsync()).First(x => x.Key == key).Value.UrlDecode().DeserializeFromJson<T>();
    }

    public static async Task RemoveCookieAsync(this IJSRuntime jsRuntime, string key)
    {
        await jsRuntime.InvokeVoidAsync("eval", $"document.cookie = \"{key}=; expires=Thu, 01 Jan 1970 00:00:01 GMT; path=/\"");
    }
    public static async Task RemoveCookieAsync<T>(this IJSRuntime jsRuntime)
    {
        var key = typeof(T).Name;
        await jsRuntime.InvokeVoidAsync("eval", $"document.cookie = \"{key}=; expires=Thu, 01 Jan 1970 00:00:01 GMT; path=/\"");
    }

}
