namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement.Content;

public abstract class BaseContentPlugin : BasePlugin
{
    [SupplyParameterFromQuery(Name = "slug")]
    protected string? ContentTypeSlug { get; set; }

    protected List<ContentDetailResponse>? Contents { get; set; }
    protected ContentTypeDetailResponse? ContentType { get; set; }
    protected FieldTypes FieldTypes { get; set; } = [];
}
