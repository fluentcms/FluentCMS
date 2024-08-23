namespace FluentCMS.Web.Plugins.Contents.Blogs;

public partial class BlogViewPlugin
{
    // #region Base

    [Inject]
    protected NavigationManager NavigationManager { get; set; } = default!;

    [Parameter]
    public string? SectionName { get; set; }

    [Parameter]
    public PluginViewState? Plugin { get; set; } = default!;

    [Inject]
    protected ApiClientFactory ApiClient { get; set; } = default!;

    [Inject]
    protected IHttpContextAccessor? HttpContextAccessor { get; set; }

    protected virtual void NavigateBack()
    {
        var url = new Uri(NavigationManager.Uri).LocalPath;
        NavigateTo(url);
    }

    protected virtual void NavigateTo(string path)
    {
        if (HttpContextAccessor?.HttpContext != null && !HttpContextAccessor.HttpContext.Response.HasStarted)
            HttpContextAccessor.HttpContext.Response.Redirect(path);
        else
            NavigationManager.NavigateTo(path);
    }

    protected virtual string GetDetailUrl(string viewName, object? parameters = null)
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
    // #endregion

    private List<BlogContent> Items { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        if (Plugin is not null)
        {
            var response = await ApiClient.PluginContent.GetAllAsync(nameof(BlogContent), Plugin.Id);

            if (response?.Data != null && response.Data.ToContentList<BlogContent>().Any())
                Items = response.Data.ToContentList<BlogContent>();

        }
    }
}
