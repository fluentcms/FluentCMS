using FluentCMS.Providers.MessageBusProviders;
using FluentCMS.Web.Plugins.Base;

namespace FluentCMS.Web.Plugins.ContentViewer;

public partial class ContentListEditPlugin
{
    [Inject]
    private IMessagePublisher MessagePublisher { get; set; } = default!;

    public const string FORM_NAME = "CONTENT_LIST_EDIT_FORM";

    private SettingsModel Model { get; set; } = default!;
    private List<ContentTypeDetailResponse> ContentTypes { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        var contentTypeResponse = await ApiClient.ContentType.GetAllAsync(ViewState.Site.Id);
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
                Template = template ?? string.Empty,
                ContentTypeSlug = slug ?? string.Empty,
            };
        }
    }

    private async Task HandleSubmit()
    {
        if (Plugin is null)
            return;

        var request = new SettingsUpdateRequest
        {
            Id = Plugin.Id,
            Settings = new Dictionary<string, string> {
                { "Template", Model.Template },
                { "ContentTypeSlug", Model.ContentTypeSlug },
            }
        };

        await ApiClient.Settings.UpdateAsync(request);

        await MessagePublisher.Publish(new Message<string>(ActionNames.InvalidateStyles, Path.Combine(ViewState.Site.Id.ToString(), ViewState.Page.Id.ToString() + ".css")));
        await OnSubmit.InvokeAsync();
    }

    class SettingsModel
    {
        public string Template { get; set; } = string.Empty;
        public string ContentTypeSlug { get; set; } = string.Empty;
    }
}
