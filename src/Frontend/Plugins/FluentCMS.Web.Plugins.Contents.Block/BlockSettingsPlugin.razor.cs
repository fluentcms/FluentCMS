namespace FluentCMS.Web.Plugins.Contents.Block;

public partial class BlockSettingsPlugin
{
    public const string PLUGIN_SETTINGS_FORM = nameof(BlockContent);

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

    private List<EditableSetting> Settings { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        if (Settings is null)
        {
            Settings = [];
            foreach (var setting in Plugin.Settings.ToList())
            {
                Settings.Add(
                    new EditableSetting
                    {
                        Key = setting.Key,
                        Value = setting.Value
                    }
                );
            }
        }
    }

    private async Task OnSubmit()
    {
        await ApiClient.Plugin.UpdateAsync(new PluginUpdateRequest
        {
            Id = Plugin.Id,
            Order = Plugin.Order, 
            Section = Plugin.Section, 
            Cols = Plugin.Cols, 
            ColsMd = Plugin.ColsMd, 
            ColsLg = Plugin.ColsLg, 
            Settings = Settings.ToDictionary(x => x.Key, x => x.Value)
        });

        NavigateBack();
    }

    public class EditableSetting
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }
}
