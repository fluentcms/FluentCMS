using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Web.Api.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public class PolicyAttribute : Attribute, IAsyncAuthorizationFilter
{
    public string Area { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;

    public PolicyAttribute(string area, string action)
    {
        Area = area;
        Action = action;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var authContext = context.HttpContext.RequestServices.GetRequiredService<IAuthContext>();
        var globalSettingService = context.HttpContext.RequestServices.GetRequiredService<IGlobalSettingsService>();

        var globalSetting = await globalSettingService.Get();

        // check if the user is super admin
        // super admin has full access to all areas and actions
        if (globalSetting?.SuperUsers != null && globalSetting.SuperUsers.Contains(authContext.Username))
            return;

        var user = await authContext.GetUser();
        var roles = await authContext.GetRoles();

        if (user == null || roles.Count == 0)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // check if roles contains the required policy
        var policies = roles.SelectMany(r => r.Policies);
        var accessibleArea = policies.Where(p => p.Area == Area).FirstOrDefault();
        if (accessibleArea == null)
        {
            context.Result = new ForbidResult();
            return;
        }

        // check if actions contains the required action
        if (!accessibleArea.Actions.Contains(Action))
        {
            context.Result = new ForbidResult();
            return;
        }
    }
}

