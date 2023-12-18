using FluentCMS.Web.UI.ApiClients;
using Microsoft.AspNetCore.Components;
using System.Web;

namespace FluentCMS.Web.UI;

public class AppStateService
{
    public AppState Current { get; }

    public AppStateService(NavigationManager navigator, SiteClient siteClient, PageClient pageClient)
    {
        try
        {
            var appState = new AppState
            {
                Host = navigator.BaseUri.Remove(navigator.BaseUri.Length - 1),
                Uri = new Uri(navigator.Uri)
            };

            var taskSite = Task.Run(() => siteClient.GetByUrlAsync(appState.Host));
            taskSite.Wait();
            appState.Site = taskSite.Result.Data;
            appState.Layout = appState.Site.Layout;

            if (appState.Site != null)
            {
                var query = HttpUtility.ParseQueryString(string.Empty);
                query["siteId"] = appState.Site.Id.ToString();
                query["path"] = appState.Uri.LocalPath;

                var taskPage = Task.Run(() => pageClient.GetByPathAsync(appState.Site.Id, appState.Uri.LocalPath));
                taskPage.Wait();
                appState.Page = taskPage.Result.Data;
            }

            if (appState.Page != null && appState.Page.Layout != null)
                appState.Layout = appState.Page.Layout;

            var queryStrs = HttpUtility.ParseQueryString(appState.Uri.Query);

            if (queryStrs["PluginId"] != null && Guid.TryParse(queryStrs["PluginId"], out var pluginId))
                if (pluginId != Guid.Empty)
                    appState.PluginId = pluginId;

            if (!string.IsNullOrEmpty(queryStrs["ViewMode"]))
                appState.ViewMode = queryStrs["ViewMode"];

            Current = appState;
        }
        catch
        {
            Current = new AppState();
        }

    }

}
