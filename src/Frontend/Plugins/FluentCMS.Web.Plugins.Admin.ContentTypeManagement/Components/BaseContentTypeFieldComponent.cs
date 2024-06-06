namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public abstract class BaseContentTypeFieldComponent : BaseComponent
{

    [Inject]
    protected IHttpClientFactory HttpClientFactory { get; set; } = default!;

    [Inject]
    protected UserLoginResponse? UserLogin { get; set; }

    [CascadingParameter]
    public ContentTypeDetailResponse? ContentType { get; set; } = default!;

    [CascadingParameter]
    public ContentTypeField? ContentTypeField { get; set; }

    [Parameter]
    public EventCallback OnComplete { get; set; }

    protected ContentTypeClient GetApiClient()
    {
        return HttpClientFactory.CreateApiClient<ContentTypeClient>(UserLogin);
    }
}
