namespace FluentCMS.Web.Plugins.RichText;

public partial class RichTextViewPlugin
{
    private RichTextContent? Item { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Plugin is not null)
        {
            var response = await ApiClient.PluginContent.GetAllAsync(nameof(RichTextContent), Plugin.Id);

            if (response?.Data != null && response.Data.ToContentList<RichTextContent>().Any())
                Item = response.Data.ToContentList<RichTextContent>().FirstOrDefault();

        }
    }
}
