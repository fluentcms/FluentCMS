using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.WebUtilities;

namespace FluentCMS.Web.UI.Plugins;

public partial class BasePlugin
{
    [Inject]
    protected NavigationManager NavigationManager { get; set; } = default!;

    [CascadingParameter]
    protected PluginDetailResponse? Plugin { get; set; } = default!;

    [CascadingParameter]
    protected PageFullDetailResponse? Page { get; set; }

    [CascadingParameter]
    protected UserLoginResponse? UserLogin { get; set; }

    [Parameter]
    public ErrorContext ErrorContext { get; set; } = new();

    [Inject]
    protected IHttpClientFactory HttpClientFactory { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        ErrorContext.Clear();
        try
        {
            await OnLoadAsync();
        }
        catch (Exception ex)
        {
            ErrorContext.SetError(ex);
        }
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

    protected TClient GetApiClient<TClient>() where TClient : class, IApiClient
    {
        return HttpClientFactory.CreateApiClient<TClient>(UserLogin);
    }
}
