using Microsoft.AspNetCore.Components;
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

    public static string? GetSlugFromQuery(this NavigationManager? navigation)
    {
        if (navigation == null)
            return default;

        var uri = new Uri(navigation.Uri);
        var query = HttpUtility.ParseQueryString(uri.Query);
        if (!string.IsNullOrEmpty(query["slug"]))
            return query["slug"];

        return default;
    }
}
