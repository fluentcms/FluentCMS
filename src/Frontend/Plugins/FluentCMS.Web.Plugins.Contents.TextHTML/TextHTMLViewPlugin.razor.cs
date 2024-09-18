namespace FluentCMS.Web.Plugins.Contents.TextHTML;

public partial class TextHTMLViewPlugin
{
    private TextHTMLContent Item { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (Plugin is not null)
        {
            var response = await ApiClient.PluginContent.GetAllAsync(nameof(TextHTMLContent), Plugin.Id);

            if (response?.Data != null && response.Data.ToContentList<TextHTMLContent>().Any())
                Item = response.Data.ToContentList<TextHTMLContent>().FirstOrDefault();
        }
    }
}
