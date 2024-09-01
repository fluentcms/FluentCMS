namespace FluentCMS.Web.Api.Controllers;

public class GlobalSettingsController(IGlobalSettingsService service, IMapper mapper) : BaseGlobalController
{
    public const string AREA = "Settings Management";
    public const string READ = "Read";
    public const string UPDATE = "Update";

    [HttpGet]
    [Policy(AREA, READ)]
    public async Task<IApiResult<GlobalSettingsResponse>> Get(CancellationToken cancellationToken = default)
    {
        var settings = await service.Get(cancellationToken) ??
            throw new AppException(ExceptionCodes.GlobalSettingsNotFound);

        var settingsResponse = mapper.Map<GlobalSettingsResponse>(settings);

        return Ok(settingsResponse);
    }

    [HttpPost]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<GlobalSettingsResponse>> Update(GlobalSettingsUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var settings = mapper.Map<GlobalSettings>(request);
        var updated = await service.Update(settings, cancellationToken);
        var settingsResponse = mapper.Map<GlobalSettingsResponse>(updated);

        return Ok(settingsResponse);
    }
}
