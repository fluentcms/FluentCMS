namespace FluentCMS.Web.Plugins.Contents.Block;

public partial class BlockInlineEditable
{
    [Inject]
    private ApiClientFactory ApiClients { get; set; } = default!;

    [Parameter]
    public BlockContent Item { get; set; } = default!;

    [Parameter]
    public PluginViewState Plugin { get; set; } = default!;

    private async Task OnSave(string content)
    {
        Item.Content = content;
        await ApiClients.PluginContent.UpdateAsync(nameof(BlockContent), Plugin.Id, Item.Id, Item.ToDictionary());
    }

    private async Task OnCancel()
    {
        await Task.CompletedTask;
    }
}
