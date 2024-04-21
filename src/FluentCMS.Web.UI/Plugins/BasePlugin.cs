﻿namespace FluentCMS.Web.UI.Plugins;

public abstract class BasePlugin : ComponentBase
{
    [Inject]
    protected NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    protected IHttpClientFactory HttpClientFactory { get; set; } = default!;

    [CascadingParameter]
    protected HttpContext HttpContext { get; set; } = default!;

    [CascadingParameter]
    public PluginDetailResponse? Plugin { get; set; } = default!;

    [CascadingParameter]
    public PageFullDetailResponse? Page { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (HttpContext is null)
            throw new ArgumentNullException(nameof(HttpContext));

        if (HttpMethods.IsPost(HttpContext.Request.Method))
        {
            await OnPostAsync();
        }
        else
        {
            await OnLoadAsync();
        }
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
        var uri = new Uri(NavigationManager.Uri).LocalPath;
        NavigationManager.NavigateTo(uri);
    }
}
