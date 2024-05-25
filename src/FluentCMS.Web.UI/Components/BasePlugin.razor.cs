using AutoMapper;
using Microsoft.AspNetCore.WebUtilities;

namespace FluentCMS.Web.UI.Plugins.Components;

public partial class BasePlugin
{
    [Inject]
    protected IMapper Mapper { get; set; } = default!;

    [Inject]
    protected NavigationManager NavigationManager { get; set; } = default!;

    [CascadingParameter]
    protected PluginDetailResponse? Plugin { get; set; } = default!;

    [CascadingParameter]
    protected PageFullDetailResponse? Page { get; set; }

    [CascadingParameter]
    protected UserLoginResponse? UserLogin { get; set; }

    [Inject]
    protected IHttpClientFactory HttpClientFactory { get; set; } = default!;

    [Inject]
    protected IHttpContextAccessor? HttpContextAccessor { get; set; }

    protected virtual void NavigateBack()
    {
        var url = new Uri(NavigationManager.Uri).LocalPath;
        NavigateTo(url);
    }

    // due to open issue in NavigationManager
    // https://github.com/dotnet/aspnetcore/issues/55685
    // https://github.com/dotnet/aspnetcore/issues/53996
    protected virtual void NavigateTo(string path)
    {
        if (HttpContextAccessor?.HttpContext != null)
            HttpContextAccessor.HttpContext.Response.Redirect(path);
        else
            NavigationManager.NavigateTo(path);
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
