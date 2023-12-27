namespace FluentCMS.Web.Api.Controllers;

public class AppTemplateController(IAppTemplateService appTemplateService, IMapper mapper) : BaseGlobalController
{
    [HttpGet]
    public async Task<IApiPagingResult<AppTemplateResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var apps = await appTemplateService.GetAll(cancellationToken);
        var appsResponse = mapper.Map<List<AppTemplateResponse>>(apps);
        return OkPaged(appsResponse);
    }
}
