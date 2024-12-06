using FluentCMS.Providers.MessageBusProviders;
using FluentCMS.Web.Plugins.Base;

namespace FluentCMS.Web.Plugins.Block;
public partial class BlockEditPlugin
{
    [Inject]
    private IMessagePublisher MessagePublisher { get; set; } = default!;

    public const string CONTENT_TYPE_NAME = nameof(BlockContent);

    [SupplyParameterFromForm(FormName = CONTENT_TYPE_NAME)]
    private BlockContent? Model { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        if (Model is null)
        {
            var response = await ApiClient.PluginContent.GetAllAsync(CONTENT_TYPE_NAME, Plugin!.Id);

            var content = response.Data!.ToContentList<BlockContent>();
            if (content is null)
                return;

            if (content!.Count == 0)
                return;

            Model = new BlockContent
            {
                Id = content![0].Id,
                Content = content![0].Content
            };
        }
    }

    private async Task OnBlockSelected(BlockDetailResponse selectedBlock)
    {
        var block = new BlockContent
        {
            Content = selectedBlock.Content ?? string.Empty
        };

        var createResponse = await ApiClient.PluginContent.CreateAsync(CONTENT_TYPE_NAME, Plugin.Id, block.ToDictionary());

        Model = new BlockContent
        {
            Id = createResponse.Data.Id,
            Content = block.Content
        };

        await MessagePublisher.Publish(new Message<string>(ActionNames.InvalidateStyles, Path.Combine(ViewState.Site.Id.ToString(), ViewState.Page.Id.ToString() + ".css")));
        await OnSubmit.InvokeAsync();
    }

    private async Task HandleSubmit()
    {
        if (Model is null || Plugin is null)
            return;

        await ApiClient.PluginContent.UpdateAsync(CONTENT_TYPE_NAME, Plugin.Id, Model.Id, Model.ToDictionary());

        await MessagePublisher.Publish(new Message<string>(ActionNames.InvalidateStyles, Path.Combine(ViewState.Site.Id.ToString(), ViewState.Page.Id.ToString() + ".css")));
        await OnSubmit.InvokeAsync();
    }
}
