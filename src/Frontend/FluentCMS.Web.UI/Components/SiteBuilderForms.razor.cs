namespace FluentCMS.Web.UI;

public partial class SiteBuilderForms
{
    [Inject]
    private ApiClientFactory ApiClient { get; set; } = default!;

    [CascadingParameter]
    private ViewState ViewState { get; set; } = default!;

    [SupplyParameterFromForm(FormName = "DeletePluginForm")]
    private Guid DeletePluginModel { get; set; } = new();

    [SupplyParameterFromForm(FormName = "CreatePluginForm")]
    private PluginCreateRequest CreatePluginModel { get; set; } = new();

    [SupplyParameterFromForm(FormName = "UpdatePluginForm")]
    private PluginUpdateRequest UpdatePluginModel { get; set; } = new();

    [SupplyParameterFromForm(FormName = "UpdatePluginOrdersForm")]
    private PageUpdatePluginOrdersRequest UpdatePluginOrdersModel { get; set; } = new();

    [SupplyParameterFromForm(FormName = "CreateBlockForm")]
    private CreateBlockRequest CreateBlockModel { get; set; } = new();

    [SupplyParameterFromForm(FormName = "UpdateBlockContentForm")]
    private UpdateBlockContentRequest UpdateBlockContentModel { get; set; } = new();

    private async Task OnCreateBlockSubmit()
    {
        var blockResponse = await ApiClient.Block.GetByIdAsync(CreateBlockModel.BlockId);

        if(blockResponse?.Data != null)
        {
            CreateBlockModel.Plugin.PageId = ViewState.Page.Id;
            CreateBlockModel.Plugin.Settings = [];
            var pluginCreateResponse = await ApiClient.Plugin.CreateAsync(CreateBlockModel.Plugin);

            var content = new Dictionary<string, object>
            {
                { "Content", blockResponse.Data.Content },
            };

            var response = await ApiClient.PluginContent.CreateAsync("BlockContent", pluginCreateResponse.Data.Id, content);
        }
    }

    private async Task OnUpdateBlockContentSubmit()
    {
        var id = UpdateBlockContentModel.Id;
        var pluginId = UpdateBlockContentModel.PluginId;
        var content = UpdateBlockContentModel.Content;

        var response = await ApiClient.PluginContent.UpdateAsync("BlockContent", pluginId, id, new Dictionary<string, object> {
            { "Content", content }
        });
    }

    private async Task OnUpdatePluginSubmit()
    {
        await ApiClient.Plugin.UpdateAsync(UpdatePluginModel);
    }

    private async Task OnUpdatePluginOrdersSubmit()
    {
        await ApiClient.Page.UpdatePluginOrdersAsync(UpdatePluginOrdersModel);
    }

    private async Task OnCreatePluginSubmit()
    {
        CreatePluginModel.PageId = ViewState.Page.Id;
        CreatePluginModel.Settings = [];

        await ApiClient.Plugin.CreateAsync(CreatePluginModel);
    }

    private async Task OnDeletePluginSubmit()
    {
        await ApiClient.Plugin.DeleteAsync(DeletePluginModel);
    }

    private class BlockFieldSetting {
        public string Type { get; set; } = string.Empty;
    }

    private class CreateBlockRequest {
        public PluginCreateRequest Plugin { get; set; }
        public Guid BlockId { get; set; } = default!;
    }
    private class UpdateBlockContentRequest {
        public Guid PluginId { get; set; }
        public Guid Id { get; set; }
        public string Content { get; set; } = string.Empty;
    }
}
