namespace FluentCMS.Web.Plugins.Contents.Block;

public partial class BlockViewPlugin
{
    public const string CONTENT_TYPE_NAME = nameof(BlockContent);

    private BlockContent? Item { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await Load();
    }

    private async Task SaveBlockContent(BlockDetailResponse selectedBlock)
    {
        var block = new BlockContent
        {
            Content = selectedBlock.Content ?? string.Empty
        };

        await ApiClient.PluginContent.CreateAsync(CONTENT_TYPE_NAME, Plugin.Id, block.ToDictionary());
        await Load();
    }

    private async Task Load()
    {
        var response = await ApiClient.PluginContent.GetAllAsync(CONTENT_TYPE_NAME, Plugin.Id);
        Item = response?.Data?.ToContentList<BlockContent>().FirstOrDefault();
    }
}
