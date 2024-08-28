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

    [SupplyParameterFromForm(FormName = PLUGIN_SETTINGS_FORM)]
    private BlockSettings? Model { get; set; } = default!;

    private BlockContent? Content { get; set; } = default!;

    private bool IsEditMode { get; set; } = false;
    // Model
    protected override async Task OnInitializedAsync()
    {
        if (Model is null)
        {
            Model = Plugin.Settings.ToPluginSettings<BlockSettings>();
        }

        if (Content is null)
        {
            var response = await ApiClient.PluginContent.GetAllAsync(PLUGIN_SETTINGS_FORM, Plugin!.Id);

            var content = response.Data.ToContentList<BlockContent>();

            if(content.Count > 0)
            {
                Content = new BlockContent
                {
                    Id = content[0].Id,
                    Template = content[0].Template,
                    Settings = content[0].Settings,
                };
            }
            else
            {                
                throw new Exception("this Plugin doesn't have any content");
            }
        }
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
