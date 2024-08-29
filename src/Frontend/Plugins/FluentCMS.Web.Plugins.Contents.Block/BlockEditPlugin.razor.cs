namespace FluentCMS.Web.Plugins.Contents.Block;
using Scriban;
using Scriban.Runtime;

public partial class BlockEditPlugin
{
    public const string CONTENT_TYPE_NAME = nameof(BlockContent);

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

    [SupplyParameterFromForm(FormName = CONTENT_TYPE_NAME)]
    private BlockContent? Model { get; set; } = default!;

    private bool IsEditMode { get; set; } = false;

    protected override async Task OnInitializedAsync()
    {
        if (Model is null)
        {
            var response = await ApiClient.PluginContent.GetAllAsync(CONTENT_TYPE_NAME, Plugin!.Id);

            var content = response.Data.ToContentList<BlockContent>();
            if(content.Count == 0)
                throw new Exception("This plugin doesn't have any content");

            Model = new BlockContent
            {
                Id = content[0].Id,
                Template = content[0].Template
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
