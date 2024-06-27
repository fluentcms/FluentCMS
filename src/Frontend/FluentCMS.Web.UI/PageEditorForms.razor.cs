namespace FluentCMS.Web.UI;

public partial class PageEditorForms
{
    #region Base Plugin
    [Inject]
    private UserLoginResponse? UserLogin { get; set; }

    [Inject]
    protected NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private IHttpClientFactory HttpClientFactory { get; set; } = default!;

    private PluginClient GetPluginClient()
    {
        return HttpClientFactory.CreateApiClient<PluginClient>(UserLogin);
    }

    [Inject]
    protected IHttpContextAccessor? HttpContextAccessor { get; set; }

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

    protected virtual void NavigateBack()
    {
        var url = new Uri(NavigationManager.Uri).LocalPath;
        NavigateTo(url);
    }

    #endregion

    [CascadingParameter]
    private ViewContext ViewContext { get; set; }

    [SupplyParameterFromForm(FormName = "UpdatePluginForm")]
    private PageEditorSaveRequest Model { get; set; } = new();

    private async Task OnUpdateSubmit()
    {
        foreach (var deletedId in Model.DeleteIds ?? [])
        {
            await GetPluginClient().DeleteAsync(deletedId);
        }

        foreach (var plugin in Model.CreatePlugins ?? [])
        {
            plugin.PageId = ViewContext.Page.Id;

            await GetPluginClient().CreateAsync(plugin);
        }

        foreach (var plugin in Model.UpdatePlugins ?? [])
        {
            await GetPluginClient().UpdateAsync(plugin);
        }

        NavigateBack();
    }

    class PageEditorNewPlugin
    {
        public Guid DefinitionId { get; set; }
        public string Section { get; set; }
        public int Order { get; set; }
    }

    class PageEditorSaveRequest
    {
        public bool Submitted { get; set; } = true;
        public List<Guid> DeleteIds { get; set; } = [];
        public List<PluginCreateRequest> CreatePlugins { get; set; } = [];
        public List<PluginUpdateRequest> UpdatePlugins { get; set; } = [];
    }
}