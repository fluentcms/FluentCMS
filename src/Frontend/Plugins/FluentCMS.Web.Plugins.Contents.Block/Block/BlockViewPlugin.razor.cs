namespace FluentCMS.Web.Plugins.Contents.Block;
using Scriban;
using Scriban.Runtime;

public partial class BlockViewPlugin
{
    public const string CONTENT_TYPE_NAME = nameof(BlockContent);

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

    [SupplyParameterFromQuery(Name = "redirectTo")]
    private string RedirectTo { get; set; } = string.Empty;

    protected virtual void NavigateBack()
    {
        if (!string.IsNullOrEmpty(RedirectTo)) {
            NavigateTo(Uri.UnescapeDataString(RedirectTo));
        }
        else
        {
            var url = new Uri(NavigationManager.Uri).LocalPath;
            NavigateTo(url);
        }
    }

    protected virtual void NavigateTo(string path)
    {
        if (HttpContextAccessor?.HttpContext != null && !HttpContextAccessor.HttpContext.Response.HasStarted)
            HttpContextAccessor.HttpContext.Response.Redirect(path);
        else
            NavigationManager.NavigateTo(path, true);
    }

    protected virtual string GetBackUrl()
    {
        if(!string.IsNullOrEmpty(RedirectTo))
        {
            return Uri.UnescapeDataString(RedirectTo);
        }
        else
        {
            return new Uri(NavigationManager.Uri).LocalPath;
        }
    }

    private string Rendered { get; set; } = string.Empty;
    private BlockContent? Item { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Plugin is not null)
        {
            var response = await ApiClient.PluginContent.GetAllAsync(nameof(BlockContent), Plugin.Id);

            if (response?.Data != null && response.Data.ToContentList<BlockContent>().Any())
                Item = response.Data.ToContentList<BlockContent>().FirstOrDefault();
        }

        var scriptObject = new ScriptObject();
        foreach (var keyValue in Item.Settings)
            scriptObject.Add(keyValue.Key, keyValue.Value);

        var context = new TemplateContext();
        context.PushGlobal(scriptObject);

        var template = Template.Parse(Item.Template);
        Rendered = template.Render(context);
    }
}
