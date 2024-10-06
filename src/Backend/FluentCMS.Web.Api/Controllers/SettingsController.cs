namespace FluentCMS.Web.Api.Controllers;

public class SettingsController(ISettingsService settingsService) : BaseGlobalController
{
    public const string AREA = "Settings Management";
    public const string READ = "Read";
    public const string UPDATE = $"Update/{READ}";

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<bool>> Update([FromBody] SettingsUpdateRequest request, CancellationToken cancellationToken = default)
    {
        await settingsService.Update(request.Id, request.Settings, cancellationToken);
        return Ok(true);
    }
}
