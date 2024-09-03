namespace FluentCMS.Web.Plugins.Contents.Block;
public partial class BlockEditPlugin
{
    public const string CONTENT_TYPE_NAME = nameof(BlockContent);

    [SupplyParameterFromForm(FormName = CONTENT_TYPE_NAME)]
    private BlockContent? Model { get; set; } = default!;

    private bool IsEditMode { get; set; } = false;

    protected override async Task OnInitializedAsync()
    {
        if (Model is null)
        {
            var response = await ApiClient.PluginContent.GetAllAsync(CONTENT_TYPE_NAME, Plugin!.Id);

            var content = response.Data.ToContentList<BlockContent>();
            if (content.Count == 0)
                throw new Exception("This plugin doesn't have any content");

            Model = new BlockContent
            {
                Id = content[0].Id,
                Content = content[0].Content
            };
            IsEditMode = true;
        }
    }

    private async Task OnSubmit()
    {
        if (Model is null || Plugin is null)
            return;

        await ApiClient.PluginContent.UpdateAsync(CONTENT_TYPE_NAME, Plugin.Id, Model.Id, Model.ToDictionary());
        NavigateBack();
    }
}
