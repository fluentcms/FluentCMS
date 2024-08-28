namespace FluentCMS.Web.UI;
using System.Text.Json;
using Microsoft.Extensions.Logging;


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

    private async Task OnUpdatePluginSubmit()
    {
        await ApiClient.Plugin.UpdateAsync(UpdatePluginModel);
    }

    private async Task OnCreateBlockSubmit()
    {
        CreateBlockModel.Plugin.PageId = ViewState.Page.Id;
        CreateBlockModel.Plugin.Settings = CreateBlockModel.Settings;

        var pluginCreateResponse = await ApiClient.Plugin.CreateAsync(CreateBlockModel.Plugin);
        var content = new Dictionary<string, object>
        {
            { "Template", CreateBlockModel.Template },
        };

        var response = await ApiClient.PluginContent.CreateAsync("BlockContent", pluginCreateResponse.Data.Id, content);
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
        public string Template { get; set; } = string.Empty;
        public Dictionary<string, string> Settings { get; set; } = [];
    }
}
