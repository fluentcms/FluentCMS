using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Web.Api.Filters;

public class ApiTokenAuthorizeFilter : IAsyncAuthorizationFilter
{
    private const string _apiTokenHearKey = "X-API-AUTH";
    private const string _anyPolicyArea = "Global";
    private const string _anyPolicyAction = "All";

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // get all PolicyAttributes for the current method
        var actionPolicies = context.ActionDescriptor.EndpointMetadata.OfType<PolicyAttribute>();

        // if no policy attributes are found or AnyPolicy is used, return
        if (!actionPolicies.Any() || actionPolicies.Any(x => x is AnyPolicyAttribute))
            return;

        // Check API Token
        var isApiTokenValid = await ValidateApiToken(context, actionPolicies);
        if (!isApiTokenValid)
        {
            context.Result = new ForbidResult();
            return;
        }

        // Checked User And Roles
        var isUserAuthorized = await AuthorizeUser(context);
        if (!isUserAuthorized)
        {
            context.Result = new UnauthorizedResult();
            return;
        }
    }

    private async Task<bool> AuthorizeUser(AuthorizationFilterContext context)
    {
        var authContext = context.HttpContext.RequestServices.GetRequiredService<IAuthContext>();

        // if user is not authenticated, return
        if (!authContext.IsAuthenticated)
            return false;

        var userService = context.HttpContext.RequestServices.GetRequiredService<IUserService>();
        var user = await userService.GetById(authContext.UserId);

        if (user == null)
            return false;


        var roleService = context.HttpContext.RequestServices.GetRequiredService<IRoleService>();
        var roles = await roleService.GetAll();

        // extract admin access roles
        //var adminRole = roles.Where(r => r.IsAdmin).FirstOrDefault();
        //if (adminRole == null)
        //    return;


        // check if user has admin access
        //if (user.RoleIds.Contains(adminRole.Id))
        //    return true;

        return true;
    }

    private async Task<bool> ValidateApiToken(AuthorizationFilterContext context, IEnumerable<PolicyAttribute> actionPolicies)
    {
        var requestApiToken = context.HttpContext.Request.Headers[_apiTokenHearKey];
        if (string.IsNullOrEmpty(requestApiToken))
            return false;

        var parts = requestApiToken.ToString().Split(':');
        if (parts.Length != 2)
            return false;

        var apiKey = parts[0];
        var apiSecret = parts[1];

        var apiTokenService = context.HttpContext.RequestServices.GetRequiredService<IApiTokenService>();

        // Check if the api key is valid
        var token = await apiTokenService.Validate(apiKey, apiSecret);

        if (token == null)
            return false;

        if (token.Policies.Any(x => x.Area == _anyPolicyArea && x.Actions.Contains(_anyPolicyAction)))
            return true;

        // Roles
        var roleService = context.HttpContext.RequestServices.GetRequiredService<IRoleService>();
        var roles = await roleService.GetAll();

        if (roles == null || !roles.Any())
            return false;


        foreach (var policy in actionPolicies)
        {
            var areaPolicyAccess = token.Policies.FirstOrDefault(x => x.Area == policy.Area);
            if (areaPolicyAccess == null)
                return false;

            var hasActionAccess = areaPolicyAccess.Actions.Contains(policy.Action);
            if (!hasActionAccess)
                return false;
        }

        //return token != null;
        return true;
    }
}

