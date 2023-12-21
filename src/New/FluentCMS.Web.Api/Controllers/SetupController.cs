using FluentCMS.Entities;
using FluentCMS.Services;
using FluentCMS.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Api.Controllers;

public class SetupController(
    SystemSettingsService systemSettingsService,
    UserService userService) : BaseController
{
    [HttpPost]
    public async Task<IApiResult<bool>> Start([FromBody] SetupStartRequest request, CancellationToken cancellationToken = default)
    {
        // creating super admin user
        var superAdmin = new User
        {
            UserName = request.Username,
            Email = request.Email
        };
        await userService.Create(superAdmin, request.Password, cancellationToken);

        // creating system settings
        var systemSettings = new SystemSettings
        {
            SuperUsers = [request.Username]
        };
        await systemSettingsService.Create(systemSettings, cancellationToken);

        return Ok(true);
    }
}
