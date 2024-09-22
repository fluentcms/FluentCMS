namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public abstract class BaseContentTypeFieldComponent : BaseComponent
{
    [Inject]
    protected ApiClientFactory ApiClient { get; set; } = default!;

    [CascadingParameter]
    public ContentTypeDetailResponse ContentType { get; set; } = default!;

    [Parameter, EditorRequired]
    public ContentTypeField ContentTypeField { get; set; } = default!;

    [CascadingParameter]
    protected FieldManagementState CurrentState { get; set; }

    [Parameter]
    public EventCallback OnComplete { get; set; }
}
