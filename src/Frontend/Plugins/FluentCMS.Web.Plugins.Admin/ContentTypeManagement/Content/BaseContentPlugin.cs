namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement.Content;

public abstract class BaseContentPlugin : BasePlugin
{
    [SupplyParameterFromQuery(Name = "slug")]
    protected string? ContentTypeSlug { get; set; }

    protected ContentTypeDetailResponse? ContentType { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (!string.IsNullOrEmpty(ContentTypeSlug))
        {
            var contentTypeResponse = await ApiClient.ContentType.GetBySlugAsync(ViewState.Site.Id, ContentTypeSlug);
            ContentType = contentTypeResponse?.Data;
        }
    }
}
