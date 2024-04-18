using FluentCMS.Shared;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.ApiClients;
public static class HttpContextCookieExtensions
{

    public static void SetCookieAsJson<T>(this HttpContext httpContext, string key, T obj)
    {
        httpContext.Response.Cookies.Append(key, obj.SerializeToJson().UrlEncode());
    }
    public static T? GetCookieAsJson<T>(this HttpContext httpContext, string key)
    {
        return httpContext.Request.Cookies[key]!.UrlDecode().DeserializeToJson<T>();
    }

    public static bool TrySetCookieAsJson<T>(this HttpContext? httpContext, string key, T obj)
    {
        try
        {
            httpContext!.SetCookieAsJson(key, obj);
            return true;
        }
        catch
        {
            return false;
        }
    }
    public static bool TrySetCookieAsJson<T>(this HttpContext? httpContext, T obj)
    {
        var key = typeof(T).Name;
        return httpContext.TrySetCookieAsJson<T>(key, obj);
    }

    public static bool TryGetCookieAsJson<T>(this HttpContext? httpContext, string key, out T? obj)
    {
        if (httpContext != null && httpContext.Request.Cookies.Keys.Any(x => x == key))
        {
            obj = httpContext.GetCookieAsJson<T>(key);
            return true;
        }
        obj = default;
        return false;
    }
    public static bool TryGetCookieAsJson<T>(this HttpContext? httpContext, out T? obj)
    {
        var key = typeof(T).Name;
        return httpContext.TryGetCookieAsJson(key, out obj);
    }
}
