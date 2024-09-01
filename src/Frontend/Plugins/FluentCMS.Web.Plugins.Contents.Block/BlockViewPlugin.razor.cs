namespace FluentCMS.Web.Plugins.Contents.Block;
using Scriban;
using Scriban.Runtime;

public partial class BlockViewPlugin
{
    public const string CONTENT_TYPE_NAME = nameof(BlockContent);

    private string Rendered { get; set; } = string.Empty;
    private BlockContent? Item { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Plugin is not null)
        {
            var response = await ApiClient.PluginContent.GetAllAsync(nameof(BlockContent), Plugin.Id);

            if (response?.Data != null && response.Data.ToContentList<BlockContent>().Any())
            {
                Item = response.Data.ToContentList<BlockContent>().FirstOrDefault();
            }
        }
    }
}
