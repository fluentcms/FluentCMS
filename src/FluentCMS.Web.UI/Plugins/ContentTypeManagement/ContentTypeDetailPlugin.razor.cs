namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement;

public partial class ContentTypeDetailPlugin
{
    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    private ContentTypeDetailResponse? ContentType { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var contentTypesResponse = await GetApiClient<ContentTypeClient>().GetByIdAsync(Id);
        ContentType = contentTypesResponse?.Data;
    }
}
