namespace FluentCMS.Web.Plugins.Contents.RichText;

public partial class RichTextEditPlugin
{
    public const string CONTENT_TYPE_NAME = nameof(RichTextContent);

    [SupplyParameterFromForm(FormName = CONTENT_TYPE_NAME)]
    private RichTextContent? Model { get; set; }

    private bool IsEditMode { get; set; } = false;

    protected override async Task OnInitializedAsync()
    {
        if (Model is null)
        {
            var response = await ApiClient.PluginContent.GetAllAsync(CONTENT_TYPE_NAME, Plugin!.Id);

            var content = response.Data?.ToContentList<RichTextContent>() ?? [];

            if (content.Count > 0)
            {
                Model = new RichTextContent
                {
                    Id = content[0].Id,
                    Content = content[0].Content,
                };
                IsEditMode = true;
            }
            else
            {
                Model = new RichTextContent
                {
                    Id = Guid.Empty
                };
            }
        }
    }

    private async Task OnSubmit()
    {
        if (Model is null || Plugin is null)
            return;

        if (IsEditMode)
            await ApiClient.PluginContent.UpdateAsync(CONTENT_TYPE_NAME, Plugin.Id, Model.Id, Model.ToDictionary());
        else
            await ApiClient.PluginContent.CreateAsync(CONTENT_TYPE_NAME, Plugin.Id, Model.ToDictionary());

        NavigateBack(true);
    }
}
