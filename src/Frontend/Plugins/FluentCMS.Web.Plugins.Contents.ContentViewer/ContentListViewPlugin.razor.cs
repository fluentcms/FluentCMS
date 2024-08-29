namespace FluentCMS.Web.Plugins.Contents.ContentViewer;
using Scriban;
using Scriban.Runtime;

public partial class ContentListViewPlugin
{
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

            if(Items != null)
            {
                var scriptObject = new ScriptObject();

                scriptObject.Add("Items", Items.Select(x => x.Data));

                var context = new TemplateContext();
                context.PushGlobal(scriptObject);

                var template = Template.Parse(Plugin.Settings["Template"] ?? "No Template");
                Rendered = template.Render(context);
            }
            // var response = await ApiClient.PluginContent.GetAllAsync(nameof(TextHTMLContent), Plugin.Id);

            // if (response?.Data != null && response.Data.ToContentList<TextHTMLContent>().Any())
            //     Content = response.Data.ToContentList<TextHTMLContent>();

        }
    }
}
