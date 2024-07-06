using FluentCMS.Web.ApiClients;

namespace FluentCMS.Web.Plugins.Contents.TextHTML;

public partial class TextHTMLViewPlugin
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

    private TextHTMLContent? Content { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Plugin is not null)
        {
            var response = await ApiClient.PluginContent.GetAllAsync(nameof(TextHTMLContent), Plugin.Id);

            if (response?.Data != null && response.Data.ToContentList<TextHTMLContent>().Any())
                Content = response.Data.ToContentList<TextHTMLContent>()[0];

        }
    }
}
