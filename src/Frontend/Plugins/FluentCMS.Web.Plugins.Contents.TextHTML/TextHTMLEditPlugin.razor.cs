namespace FluentCMS.Web.Plugins.Contents.TextHTML;

public partial class TextHTMLEditPlugin
{
    public const string CONTENT_TYPE_NAME = nameof(TextHTMLContent);

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

    [SupplyParameterFromForm(FormName = CONTENT_TYPE_NAME)]
    private TextHTMLContent? Model { get; set; }

    [SupplyParameterFromQuery(Name = nameof(Id))]
    private Guid? Id { get; set; } = default!;

    protected virtual string GetBackUrl()
    {
        var uri = new Uri(NavigationManager.Uri);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        var redirectTo = query["redirectTo"];

        if (!string.IsNullOrEmpty(redirectTo))
        {
            return Uri.UnescapeDataString(redirectTo);
        }
        else
        {
            return uri.LocalPath;
        }
    }

    protected override async Task OnInitializedAsync()
    {
        if (Model is null)
        {
            if (Id != null)
            {
                var response = await ApiClient.PluginContent.GetByIdAsync(CONTENT_TYPE_NAME, Plugin!.Id, Id.Value);

                var content = response.Data.Data.ToContent<TextHTMLContent>();

                Model = new TextHTMLContent
                {
                    Id = Plugin!.Id,
                    Content = content.Content,
                    IsRichText = content.IsRichText,
                };
            }
            else
            {
                Model = new TextHTMLContent
                {
                    Id = Plugin!.Id
                };
            }
        }
    }

    private async Task OnSubmit()
    {
        if (Model is null || Plugin is null)
            return;

        if (Id is null)
            await ApiClient.PluginContent.CreateAsync(CONTENT_TYPE_NAME, Plugin.Id, Model.ToDictionary());
        else
            await ApiClient.PluginContent.UpdateAsync(CONTENT_TYPE_NAME, Plugin.Id, Id.Value, Model.ToDictionary());

        NavigateBack();
    }
}
