using FluentCMS.Web.ApiClients;

namespace FluentCMS.Web.Plugins.Contents.TextHTML;

public partial class TextHTMLViewPlugin
{
    private TextHTMLContent? Content { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Plugin is not null)
        {
            var response = await GetApiClient<PluginContentClient>().GetAllAsync(nameof(TextHTMLContent), Plugin.Id);

            if (response?.Data != null && response.Data.ToContentList<TextHTMLContent>().Any())
                Content = response.Data.ToContentList<TextHTMLContent>()[0];

        }
    }
}
