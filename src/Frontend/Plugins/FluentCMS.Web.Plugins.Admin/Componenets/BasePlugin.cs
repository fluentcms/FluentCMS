namespace FluentCMS.Web.Plugins;

public abstract class BasePlugin : ComponentBase
{
    [Inject]
    protected IMapper Mapper { get; set; } = default!;

    [Inject]
    protected NavigationManager NavigationManager { get; set; } = default!;

    [CascadingParameter]
    public ViewState? ViewState { get; set; } = default!;

    [Parameter]
    public PluginViewState? Plugin { get; set; } = default!;

    [Parameter]
    public PluginDefinitionTypeViewState DefinitionType { get; set; } = default!;

    [Inject]
    protected ApiClientFactory ApiClient { get; set; } = default!;

    [Inject]
    protected IHttpContextAccessor? HttpContextAccessor { get; set; }

    protected virtual void NavigateBack()
    {
        var uri = new Uri(NavigationManager.Uri);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        var redirectTo = query["redirectTo"];

        if (!string.IsNullOrEmpty(redirectTo))
        {
            NavigateTo(Uri.UnescapeDataString(redirectTo));
        }
        else
        {
            var url = uri.LocalPath;
            NavigateTo(url);
        }
    }

    // due to open issue in NavigationManager
    // https://github.com/dotnet/aspnetcore/issues/55685
    // https://github.com/dotnet/aspnetcore/issues/53996
    protected virtual void NavigateTo(string path)
    {
        if (HttpContextAccessor?.HttpContext != null && !HttpContextAccessor.HttpContext.Response.HasStarted)
            HttpContextAccessor.HttpContext.Response.Redirect(path);
        else
            NavigationManager.NavigateTo(path);
    }

    protected virtual string GetUrl(string viewName, object? parameters = null, bool redirectToCurrentPage = true)
    {
        var uri = new Uri(NavigationManager.Uri);
        var oldQueryParams = HttpUtility.ParseQueryString(uri.Query);

        // this gets the page path from root without QueryString
        var pagePathWithoutQueryString = uri.GetLeftPart(UriPartial.Path);

        var newQueryParams = new Dictionary<string, string?>()
        {
            { "pluginId", Plugin!.Id.ToString() },
            { "viewName", viewName }
        };

        if (redirectToCurrentPage)
        {
            oldQueryParams.Remove("redirectTo");

            var updatedQuery = string.Join("&", oldQueryParams.AllKeys.Select(key => $"{key}={Uri.EscapeDataString(oldQueryParams[key])}"));

            var newPathAndQuery = $"{uri.LocalPath}{(string.IsNullOrEmpty(updatedQuery) ? string.Empty : "?" + updatedQuery)}";

            newQueryParams["redirectTo"] = Uri.EscapeDataString(newPathAndQuery);
        }

        if (parameters != null)
        {
            foreach (var propInfo in parameters.GetType().GetProperties())
                newQueryParams[propInfo.Name] = propInfo.GetValue(parameters)?.ToString();
        }

        foreach (var key in oldQueryParams.AllKeys)
        {
            if (string.IsNullOrEmpty(key) || newQueryParams.ContainsKey(key))
                continue;

            newQueryParams[key] = oldQueryParams[key];
        }

        return QueryHelpers.AddQueryString(pagePathWithoutQueryString, newQueryParams);
    }
}
