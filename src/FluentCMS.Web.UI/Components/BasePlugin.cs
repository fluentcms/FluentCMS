using AutoMapper;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Web.UI.Plugins.Components;

public abstract class BasePlugin : ComponentBase
{
    [Inject]
    protected IMapper Mapper { get; set; } = default!;

    [Inject]
    protected NavigationManager NavigationManager { get; set; } = default!;

    [Parameter]
    public Guid? PluginId { get; set; }

    [Parameter]
    public string? SectionName { get; set; }

    [CascadingParameter]
    protected PluginDetailResponse? Plugin { get; set; } = default!;

    //[CascadingParameter]
    //protected Task<AuthenticationState> AuthenticationStateTask { get; set; } = default!;

    [Inject]
    protected UserLoginResponse? UserLogin { get; set; }

    [Inject]
    protected IHttpClientFactory HttpClientFactory { get; set; } = default!;

    [Inject]
    protected IHttpContextAccessor? HttpContextAccessor { get; set; }

    protected override async Task OnInitializedAsync()
    {
        // if the plugin is interactive, we should do this due to open issue
        // https://github.com/dotnet/aspnetcore/issues/53482
        //UserLogin = await AuthenticationStateTask.GetLogin();
    }

    protected override Task OnParametersSetAsync()
    {
        return base.OnParametersSetAsync();
    }

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
        if (HttpContextAccessor?.HttpContext != null && !HttpContextAccessor.HttpContext.Response.HasStarted)
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
