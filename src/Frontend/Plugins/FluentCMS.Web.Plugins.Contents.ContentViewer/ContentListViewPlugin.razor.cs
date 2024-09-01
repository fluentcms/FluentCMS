using FluentCMS.Providers.TemplateRenderingProviders.Abstractions;

namespace FluentCMS.Web.Plugins.Contents.ContentViewer;

public partial class ContentListViewPlugin
{
    [Inject]
    protected ITemplateRenderingProvider RenderingProvider { get; set; } = default!;

    [Inject]
    protected NavigationManager NavigationManager { get; set; } = default!;

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

    private List<ContentDetailResponse> Items { get; set; } = default!;
    private string Rendered { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        if (Plugin is not null)
        {
            if (Plugin.Settings.TryGetValue("ContentTypeSlug", out var slug) && !string.IsNullOrEmpty(slug))
            {
                var response = await ApiClient.Content.GetAllAsync(slug);
                if (response.Data != null)
                {
                    Items = response.Data.ToList();
                }
            }

            if (Items != null)
            {
                var data = new Dictionary<string, object>
                {
                    { "Items", Items.Select(x => x.Data) }
                };
                var content = Plugin.Settings["Template"] ?? "No Template";
                Rendered = RenderingProvider.Render(content, data);
            }
        }
    }
}
