namespace FluentCMS.Web.Api.Controllers;

public class GlobalSettingsController(IGlobalSettingsService service, IMapper mapper) : BaseGlobalController
{
    public const string AREA = "Settings Management";
    public const string READ = "Read";
    public const string UPDATE = "Update";

    [HttpGet]
    [Policy(AREA, READ)]
    public async Task<IApiResult<GlobalSettings>> Get(CancellationToken cancellationToken = default)
    {
        var systemSettings = await service.Get(cancellationToken) ??
            throw new AppException(ExceptionCodes.GlobalSettingsNotFound);

        return Ok(systemSettings);
    }

    [HttpPost]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<Models.GlobalSettingsDetailResponse>> Update(GlobalSettingsUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var settings = mapper.Map<GlobalSettings>(request);

        var updated = await service.Update(settings, cancellationToken);

        var response = mapper.Map<GlobalSettingsDetailResponse>(updated);

        return Ok(response);
    }
}
