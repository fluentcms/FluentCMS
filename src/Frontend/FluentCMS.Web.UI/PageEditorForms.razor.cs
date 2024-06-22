namespace FluentCMS.Web.UI;

public partial class PageEditorForms {
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

    [SupplyParameterFromForm(FormName="CreatePluginForm")]
    private PluginCreateRequest CreateModel { get; set; } = new();

    [SupplyParameterFromForm(FormName="UpdatePluginForm")]
    private List<PluginUpdateRequest> UpdateModel { get; set; } = new List<PluginUpdateRequest>();

    [SupplyParameterFromForm(FormName="DeletePluginForm")]
    private Guid DeleteModel { get; set; } = Guid.Empty;

    private async Task OnCreateSubmit()
    {
        await GetPluginClient().CreateAsync(CreateModel);
        NavigateBack();
    }

    private async Task OnUpdateSubmit()
    {
        foreach (var updateRequest in UpdateModel)
        {
            await GetPluginClient().UpdateAsync(updateRequest);
        }
        NavigateBack();
    }

    private async Task OnDeleteSubmit()
    {
        await GetPluginClient().DeleteAsync(DeleteModel);
        NavigateBack();
    }
}