using Microsoft.AspNetCore.Components;
using System.Web;

namespace FluentCMS.Web.UI;

public class SiteContext
{
    public SiteDetailResponse Site { get; set; } = default!;
    public PageDetailResponse Page { get; set; } = default!;

    public SiteContext(NavigationManager navigator, SiteClient siteClient, PageClient pageClient)
    {
        var url = navigator.BaseUri.Remove(navigator.BaseUri.Length - 1);
        var uri = new Uri(navigator.Uri);

        var taskSite = Task.Run(() => siteClient.GetByUrlAsync(url));
        taskSite.Wait();
        Site = taskSite.Result.Data;

        if (Site != null)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            query["siteId"] = Site.Id.ToString();
            query["path"] = uri.LocalPath;

            var taskPage = Task.Run(() => pageClient.GetByPathAsync(url, uri.LocalPath));
            taskPage.Wait();
            Page = taskPage.Result.Data;
        }
    }
}
