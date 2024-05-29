namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement;

public partial class ContentTypeListPlugin
{
    private List<ContentTypeDetailResponse> ContentTypes { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        var contentTypesResponse = await GetApiClient<ContentTypeClient>().GetAllAsync();
        ContentTypes = contentTypesResponse?.Data?.ToList() ?? [];
    }
}
