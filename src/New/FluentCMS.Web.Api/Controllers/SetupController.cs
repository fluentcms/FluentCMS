using FluentCMS.Entities;
using FluentCMS.Services;
using FluentCMS.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace FluentCMS.Web.Api.Controllers;

[Route("sys/api/[controller]/[action]")]
public class SetupController : BaseController
{

    private static bool? _initialized;

    private readonly SetupSettings _setupSettings;
    private readonly IGlobalSettingsService _globalSettingsService;
    private readonly IUserService _userService;

    public SetupController(IConfiguration configuration, IGlobalSettingsService globalSettingsService, IUserService userService)
    {
        _setupSettings = configuration.GetInstance<SetupSettings>("SetupSettings") ??
            throw new AppException(ExceptionCodes.SetupSettingsNotDefined);

        if (_setupSettings.SuperAdmin == null)
            throw new AppException(ExceptionCodes.SetupSettingsSuperAdminNotDefined);

        _globalSettingsService = globalSettingsService;
        _userService = userService;
    }

    [HttpGet]
    public async Task<IApiResult<bool>> IsInitialized()
    {
        // Check initialization status only once, in the beginning
        if (!_initialized.HasValue)
            _initialized = await InitCondition();

        return Ok(_initialized.Value);
    }

    [HttpPost]
    public async Task<IApiResult<GlobalSettings>> Start()
    {
        // Check if this is the first time setup or not
        if (_initialized.HasValue && _initialized.Value)
            throw new AppException(ExceptionCodes.SetupSettingsAlreadyInitialized);

        if (await InitCondition())
        {
            _initialized = true;
            throw new AppException(ExceptionCodes.SetupSettingsAlreadyInitialized);
        }

        // Creating super admin user
        var superAdmin = new User
        {
            UserName = _setupSettings.SuperAdmin.Username,
            Email = _setupSettings.SuperAdmin.Email,
        };

        await _userService.Create(superAdmin, _setupSettings.SuperAdmin.Password);

        // Creating default global settings
        var settings = new GlobalSettings
        {
            SuperUsers = [_setupSettings.SuperAdmin.Username],
        };

        return Ok(await _globalSettingsService.Init(settings));
    }

    private async Task<bool> InitCondition()
    {
        // Check if there is any user in the system.
        // Also, check if the system is already initialized or not.
        return await _globalSettingsService.Get() != null && await _userService.Any();
    }
}
