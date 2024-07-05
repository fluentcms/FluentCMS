using FluentCMS.Web.ApiClients;

namespace FluentCMS.Web.Plugins.Contents.TextHTML;

public partial class TextHTMLViewPlugin
{
    private TextHTMLContent? Content { get; set; }

    [Inject]
    protected ApiClientFactory ApiClient { get; set; } = default!;

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
