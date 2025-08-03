using FluentCMS.Providers.MessageBusProviders;
using FluentCMS.Web.Plugins.Base;

namespace FluentCMS.Web.Plugins.Block;

public partial class BlockViewPlugin
{
    [Inject]
    private IMessagePublisher MessagePublisher { get; set; } = default!;

    public const string CONTENT_TYPE_NAME = nameof(BlockContent);

    private BlockContent? Item { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await Load();
    }

    private async Task OnCancel()
    {
        StateHasChanged();
        await Task.CompletedTask;
    }

    private async Task UpdateContent(string content)
    {
        if (Item is null)
            return;

        Item.Content = content;
        await ApiClient.PluginContent.UpdateAsync(CONTENT_TYPE_NAME, Plugin.Id, Item.Id, Item.ToDictionary());
        await MessagePublisher.Publish(new Message<string>(ActionNames.InvalidateStyles, Path.Combine(ViewState.Site.Id.ToString(), ViewState.Page.Id.ToString() + ".css")));
    }

    private async Task SaveBlockContent(BlockDetailResponse selectedBlock)
    {
        var block = new BlockContent
        {
            Content = selectedBlock.Content ?? string.Empty
        };

        await ApiClient.PluginContent.CreateAsync(CONTENT_TYPE_NAME, Plugin.Id, block.ToDictionary());
        await MessagePublisher.Publish(new Message<string>(ActionNames.InvalidateStyles, Path.Combine(ViewState.Site.Id.ToString(), ViewState.Page.Id.ToString() + ".css")));
        await Load();
    }

    private async Task Load()
    {
        var response = await ApiClient.PluginContent.GetAllAsync(CONTENT_TYPE_NAME, Plugin.Id);
        Item = response?.Data?.ToContentList<BlockContent>().FirstOrDefault();
    }
}
