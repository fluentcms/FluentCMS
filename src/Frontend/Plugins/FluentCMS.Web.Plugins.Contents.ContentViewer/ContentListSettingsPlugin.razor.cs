namespace FluentCMS.Web.Plugins.Contents.ContentViewer;

public partial class ContentListSettingsPlugin
{
    public const string FORM_NAME = "CONTENT_LIST_EDIT_FORM";

    [Inject]
    protected NavigationManager NavigationManager { get; set; } = default!;

    [Parameter]
    public PluginDefinitionTypeViewState? DefinitionType { get; set; } = default!;

    [Parameter]
    public PluginViewState? Plugin { get; set; } = default!;

    [Inject]
    protected ApiClientFactory ApiClient { get; set; } = default!;

    [Inject]
    protected IHttpContextAccessor? HttpContextAccessor { get; set; }

    [SupplyParameterFromQuery(Name = "redirectTo")]
    private string RedirectTo { get; set; } = string.Empty;

    protected virtual void NavigateBack()
    {
        if (!string.IsNullOrEmpty(RedirectTo))
        {
            NavigateTo(Uri.UnescapeDataString(RedirectTo));
        }
        else
        {
            var url = new Uri(NavigationManager.Uri).LocalPath;
            NavigateTo(url);
        }
    }

    protected virtual void NavigateTo(string path)
    {
        if (HttpContextAccessor?.HttpContext != null && !HttpContextAccessor.HttpContext.Response.HasStarted)
            HttpContextAccessor.HttpContext.Response.Redirect(path);
        else
            NavigationManager.NavigateTo(path, true);
    }

    protected virtual string GetBackUrl()
    {
        if (!string.IsNullOrEmpty(RedirectTo))
        {
            return Uri.UnescapeDataString(RedirectTo);
        }
        else
        {
            return new Uri(NavigationManager.Uri).LocalPath;
        }
    }

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

        var request = new PluginUpdateRequest
        {
            Id = Plugin.Id,
            Order = Plugin.Order,
            Section = Plugin.Section,
            Cols = Plugin.Cols,
            ColsMd = Plugin.ColsMd,
            ColsLg = Plugin.ColsLg,
            Settings = new Dictionary<string, string> {
                { "Template", Model.Template },
                { "ContentTypeSlug", Model.ContentTypeSlug },
            }
        };

        await ApiClient.Plugin.UpdateAsync(request);

        NavigateBack();
    }

    class SettingsModel
    {
        public string Template { get; set; } = string.Empty;
        public string ContentTypeSlug { get; set; } = string.Empty;
    }
}
