namespace FluentCMS.Web.Plugins.Contents.ContactUs;

public partial class ContactUsSettingsPlugin
{
    public const string PLUGIN_SETTINGS_FORM = nameof(ContactUsSettings);

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

    protected virtual void NavigateBack()
    {
        var url = new Uri(NavigationManager.Uri).LocalPath;
        NavigateTo(url + "?pageEdit=true");
    }

    protected virtual void NavigateTo(string path)
    {
        if (HttpContextAccessor?.HttpContext != null && !HttpContextAccessor.HttpContext.Response.HasStarted)
            HttpContextAccessor.HttpContext.Response.Redirect(path);
        else
            NavigationManager.NavigateTo(path, true);
    }

    [SupplyParameterFromForm(FormName = PLUGIN_SETTINGS_FORM)]
    private ContactUsSettings? Model { get; set; }

    [SupplyParameterFromQuery(Name = nameof(Id))]
    private Guid? Id { get; set; } = default!;

    protected virtual string GetBackUrl()
    {
        return new Uri(NavigationManager.Uri).LocalPath;
    }

    private List<ContactUsContent> Items { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        Model = Plugin.Settings.ToPluginSettings<ContactUsSettings>();

        var response = await ApiClient.PluginContent.GetAllAsync(nameof(ContactUsContent), Plugin!.Id);
        Items = response.Data.ToContentList<ContactUsContent>();

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
