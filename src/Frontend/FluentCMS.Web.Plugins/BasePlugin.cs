using AutoMapper;
using Microsoft.AspNetCore.WebUtilities;
using System.Web;

namespace FluentCMS.Web.Plugins;

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

    [Parameter]
    public PluginDetailResponse? Plugin { get; set; } = default!;

    [Inject]
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
        var uri = new Uri(NavigationManager.Uri);
        var oldQueryParams = HttpUtility.ParseQueryString(uri.Query);

        // this gets the page path from root without QueryString
        var pagePathWithoutQueryString = uri.GetLeftPart(UriPartial.Path);

        var newQueryParams = new Dictionary<string, string?>()
        {
            { "pluginDef", pluginDefName },
            { "typeName", viewTypeName }
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

    protected TClient GetApiClient<TClient>() where TClient : class, IApiClient
    {
        return HttpClientFactory.CreateApiClient<TClient>(UserLogin);
    }
}
