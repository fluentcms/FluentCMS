namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class ContentListPlugin
{
    [SupplyParameterFromQuery(Name = "slug")]
    private string ContentTypeSlug { get; set; }

    private List<ContentDetailResponse> Contents { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        var contentsResponse = await GetApiClient<ContentClient>().GetAllAsync(ContentTypeSlug);
        Contents = contentsResponse?.Data?.ToList() ?? [];
    }
}
