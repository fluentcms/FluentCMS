using Microsoft.AspNetCore.WebUtilities;

namespace FluentCMS.Web.UI.Plugins;

public partial class BasePlugin
{
    [Inject]
    protected NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    protected IAuthService AuthService { get; set; } = default!;

    [CascadingParameter]
    protected HttpContext HttpContext { get; set; } = default!;

    [CascadingParameter]
    public PluginDetailResponse? Plugin { get; set; } = default!;

    [CascadingParameter]
    public PageFullDetailResponse? Page { get; set; }
    public UserLoginResponse? CurrentUser { get; private set; }

    protected override async Task OnInitializedAsync()
    {
        CurrentUser = await AuthService.GetLogin();
        await OnLoadAsync();
    }

    protected virtual async Task OnPostAsync()
    {
        await Task.CompletedTask;
    }

    protected virtual async Task OnLoadAsync()
    {
        await Task.CompletedTask;
    }

    protected virtual void NavigateBack()
    {
        var url = new Uri(NavigationManager.Uri).LocalPath;
        NavigationManager.NavigateTo(url);
    }

    protected virtual string GetUrl(string viewTypeName, object? parameters = null)
    {
        return GetUrl(Plugin?.Definition.Name ?? "/", viewTypeName, parameters);
    }

    protected virtual string GetUrl(string pluginDefName, string viewTypeName, object? parameters = null)
    {
        var queryParams = new Dictionary<string, string?>()
        {
            { "pluginDef", pluginDefName },
            { "typeName", viewTypeName }
        };

        if (parameters != null)
        {
            foreach (var propInfo in parameters.GetType().GetProperties())
                queryParams[propInfo.Name] = propInfo.GetValue(parameters)?.ToString();
        }
        return QueryHelpers.AddQueryString(NavigationManager.Uri, queryParams);
    }
}
