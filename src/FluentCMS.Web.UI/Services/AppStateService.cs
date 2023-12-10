using FluentCMS.Web.UI.ApiClients;
using Microsoft.AspNetCore.Components;
using System.Web;

namespace FluentCMS.Web.UI;

public class AppStateService(
    NavigationManager navigator,
    SiteClient siteClient,
    PageClient pageClient)
{
    public async Task<AppState?> GetAppState(CancellationToken cancellationToken = default)
    {
        try
        {
            var appState = new AppState
            {
                Host = navigator.BaseUri.Remove(navigator.BaseUri.Length - 1),
                Uri = new Uri(navigator.Uri)
            };

            appState.Site = await siteClient.GetByUrl(appState.Host, cancellationToken);
            appState.Layout = appState.Site?.Layout;

            if (appState.Site != null)
            {
                var query = HttpUtility.ParseQueryString(string.Empty);
                query["siteId"] = appState.Site.Id.ToString();
                query["path"] = appState.Uri.LocalPath;
                appState.Page = await pageClient.GetByPath(appState.Site.Id, appState.Uri.LocalPath, cancellationToken);
            }

            if (appState.Page != null && appState.Page.Layout != null)
                appState.Layout = appState.Page.Layout;

            var queryStrs = HttpUtility.ParseQueryString(appState.Uri.Query);

            if (queryStrs["PluginId"] != null && Guid.TryParse(queryStrs["PluginId"], out var pluginId))
                if (pluginId != Guid.Empty)
                    appState.PluginId = pluginId;

            if (!string.IsNullOrEmpty(queryStrs["ViewMode"]))
                appState.ViewMode = queryStrs["ViewMode"];

            return appState;
        }
        catch
        {
            return null;
        }
    }
}
