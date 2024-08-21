namespace FluentCMS.Web.Plugins.Contents.GoogleMap;

public partial class GoogleMapSettingsPlugin
{
    public const string PLUGIN_SETTINGS_FORM = nameof(GoogleMapSettings);

    [Inject]
    protected NavigationManager NavigationManager { get; set; } = default!;

    [Parameter]
    public string? SectionName { get; set; }

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
        if (!string.IsNullOrEmpty(RedirectTo)) {
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
        if(!string.IsNullOrEmpty(RedirectTo))
        {
            return Uri.UnescapeDataString(RedirectTo);
        }
        else
        {
            return new Uri(NavigationManager.Uri).LocalPath;
        }
    }

    [SupplyParameterFromForm(FormName = PLUGIN_SETTINGS_FORM)]
    private GoogleMapSettings? Model { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Model = Plugin.Settings.ToPluginSettings<GoogleMapSettings>();
    }

    private async Task OnSubmit()
    {
        await ApiClient.Plugin.UpdateAsync(new PluginUpdateRequest {
            Id = Plugin.Id,
            Settings = Model.ToDictionary(),
            Order = Plugin.Order,
            Section = Plugin.Section,
            Cols = Plugin.Cols,
            ColsMd = Plugin.ColsMd,
            ColsLg = Plugin.ColsLg,
        });

        NavigateBack();
    }
}
