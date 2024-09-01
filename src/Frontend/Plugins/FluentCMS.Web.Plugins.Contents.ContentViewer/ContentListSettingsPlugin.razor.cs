namespace FluentCMS.Web.Plugins.Contents.ContentViewer;

public partial class ContentListSettingsPlugin
{
    public const string FORM_NAME = "CONTENT_LIST_EDIT_FORM";

    private SettingsModel Model { get; set; } = default!;
    private List<ContentTypeDetailResponse> ContentTypes { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        var contentTypeResponse = await ApiClient.ContentType.GetAllAsync();
        if (contentTypeResponse?.Data != null)
        {
            ContentTypes = contentTypeResponse.Data.ToList();
        }

        if (Model is null)
        {
            Plugin.Settings.TryGetValue("ContentTypeSlug", out var slug);
            Plugin.Settings.TryGetValue("Template", out var template);

            Model = new()
            {
                Template = template,
                ContentTypeSlug = slug,
            };
        }
    }

    private async Task OnSubmit()
    {
        if (Plugin is null)
            return;

        var request = new PluginUpdateSettingsRequest
        {
            Id = Plugin.Id,
            Settings = new Dictionary<string, string> {
                { "Template", Model.Template },
                { "ContentTypeSlug", Model.ContentTypeSlug },
            }
        };

        await ApiClient.Plugin.UpdateSettingsAsync(request);

        NavigateBack();
    }

    class SettingsModel
    {
        public string Template { get; set; } = string.Empty;
        public string ContentTypeSlug { get; set; } = string.Empty;
    }
}
