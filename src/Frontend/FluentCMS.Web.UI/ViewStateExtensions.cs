using AutoMapper;
using FluentCMS;
using System.Web;

namespace Microsoft.Extensions.DependencyInjection;

public static class ViewStateExtensions
{
    public static void Load(this ViewState viewState, IServiceProvider sp)
    {
        var navigationManager = sp.GetRequiredService<NavigationManager>();
        var apiClient = sp.GetRequiredService<ApiClientFactory>();
        var mapper = sp.GetRequiredService<IMapper>();

        var pageResponse = apiClient.Page.GetByUrlAsync(navigationManager.Uri).GetAwaiter().GetResult();

        if (pageResponse?.Data == null)
            throw new Exception("Error while loading ViewState");

        viewState.Page = mapper.Map<PageViewState>(pageResponse.Data);
        viewState.Layout = mapper.Map<LayoutViewState>(pageResponse.Data.Layout);
        viewState.DetailLayout = mapper.Map<LayoutViewState>(pageResponse.Data.DetailLayout);
        viewState.EditLayout = mapper.Map<LayoutViewState>(pageResponse.Data.EditLayout);
        viewState.Site = mapper.Map<SiteViewState>(pageResponse.Data.Site);
        viewState.Plugins = pageResponse.Data.Sections!.Values.SelectMany(x => x).Select(p => mapper.Map<PluginViewState>(p)).ToList();
        viewState.User = mapper.Map<UserViewState>(pageResponse.Data.User);

        // check if the page is in edit mode
        // it should have pluginId and pluginViewName query strings
        var uriBuilder = new UriBuilder(navigationManager.Uri);
        var queryParams = HttpUtility.ParseQueryString(uriBuilder.Query);
        if (queryParams["pluginId"] != null && queryParams["viewName"] != null)
        {
            // check if the pluginId is valid
            if (Guid.TryParse(queryParams["pluginId"], out var pluginId))
            {
                // TODO: Decide when show edit and when show detail view
                if (queryParams["viewMode"] == "detail")
                {
                    viewState.Type = ViewStateType.PluginDetail;
                }
                else
                {
                    viewState.Type = ViewStateType.PluginEdit;
                }
                viewState.Plugin = viewState.Plugins.Single(x => x.Id == pluginId);
                viewState.PluginViewName = queryParams["viewName"];
            }
        }

        if (queryParams["pageEdit"] != null)
            viewState.Type = ViewStateType.PageEdit;

        if (queryParams["pagePreview"] != null)
            viewState.Type = ViewStateType.PagePreview;

        viewState.StateChanged();
    }
}

