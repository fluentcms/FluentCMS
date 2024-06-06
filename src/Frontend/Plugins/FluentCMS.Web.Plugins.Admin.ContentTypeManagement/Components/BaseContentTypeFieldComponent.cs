namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public abstract class BaseContentTypeFieldComponent : BaseComponent
{

    [Inject]
    protected IHttpClientFactory HttpClientFactory { get; set; } = default!;

    [Inject]
    protected UserLoginResponse? UserLogin { get; set; }

    [Parameter]
    public ContentTypeDetailResponse? ContentType { get; set; }

    [Parameter]
    public EventCallback OnComplete { get; set; }

    protected ContentTypeClient GetApiClient()
    {
        return HttpClientFactory.CreateApiClient<ContentTypeClient>(UserLogin);
    }
}
