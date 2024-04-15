using Microsoft.AspNetCore.Components;
using System;
using System.Web;

namespace FluentCMS.Web.UI;

public static class Helper
{
    public static Guid? GetIdFromQuery(this NavigationManager? navigation)
    {
        if (navigation == null)
            return default;

        var uri = new Uri(navigation.Uri);
        var query = HttpUtility.ParseQueryString(uri.Query);
        if (Guid.TryParse(query["id"], out var id))
            return id;

        return default;
    }

    public static string? GetStringFromQuery(this NavigationManager? navigation, string key)
    {
        if (navigation == null)
            return default;

        var uri = new Uri(navigation.Uri);
        var query = HttpUtility.ParseQueryString(uri.Query);
        if (!string.IsNullOrEmpty(query[key]))
            return query[key];

        return default;
    }

    public static string Plugin(this NavigationManager? navigationManager, string pluginDef, string typeName, object? parameters = null)
    {
        parameters ??= new { };

        var properties = parameters.GetType()
                                       .GetProperties()
                                       .Where(p => p.GetValue(parameters, null) != null)
                                       .Select(p => $"{p.Name}={HttpUtility.UrlEncode(p.GetValue(parameters, null)?.ToString() ?? "")}");

        var queries = String.Join("&", properties.ToArray());

        queries = string.IsNullOrEmpty(queries) ? queries : "&" + queries;

        var absolutePath = new Uri(navigationManager.Uri).AbsolutePath;

        return $"{absolutePath}?pluginDef={pluginDef}&typeName={typeName}{queries}";
    }
}
