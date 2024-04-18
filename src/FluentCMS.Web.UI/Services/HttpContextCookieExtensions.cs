using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.UI.Services;
public static class HttpContextCookieExtensions
{
    private static string SerializeToJson<T>(this T obj)
    {
        return JsonSerializer.Serialize(obj);
    }

    private static T? DeserializeToJson<T>(this string json)
    {
        return JsonSerializer.Deserialize<T?>(json);
    }

    private static string UrlEncode(this string s)
    {
        return HttpUtility.UrlEncode(s);
    }

    private static string UrlDecode(this string s)
    {
        return HttpUtility.UrlDecode(s);
    }
    public static void SetJsonCookie<T>(this HttpContext httpContext, string key, T obj)
    {
        httpContext.Response.Cookies.Append(key, obj.SerializeToJson().UrlEncode());
    }
    public static T? GetJsonCookie<T>(this HttpRequest httpRequest, string key)
    {
        return httpRequest.Cookies[key]!.UrlDecode().DeserializeToJson<T>();
    }

    public static bool TrySetJsonCookie<T>(this HttpContext? httpContext, string key, T obj)
    {
        if (httpContext is { })
        {
            httpContext.Response.Cookies.Append(key, obj.SerializeToJson().UrlEncode());
            return true;
        }
        return false;
    }

    public static bool TryGetJsonCookie<T>(this HttpRequest? httpRequest, string key, out T? obj)
    {
        if (httpRequest.Cookies.Keys.Any(x => x == key))
        {
            obj = httpRequest.GetJsonCookie<T>(key);
            return true;
        }
        obj = default(T);
        return false;
    }

}
