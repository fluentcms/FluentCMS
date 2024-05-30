using Microsoft.AspNetCore.Mvc.Filters;

namespace FluentCMS.Web.Api.Filters;

public class PolicyAuthorizeFiler : IAsyncAuthorizationFilter
{
    private readonly IAuthContext _authContext;
    private readonly IUserService _userService;
    private readonly IRoleService _roleService;

    public PolicyAuthorizeFiler(IAuthContext authContext, IUserService userService, IRoleService roleService)
    {
        _authContext = authContext;
        _userService = userService;
        _roleService = roleService;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // get all PolicyAttributes for the current method
        var policyAttributes = context.ActionDescriptor.EndpointMetadata.OfType<PolicyAttribute>();

        // if no policy attributes are found, return
        if (!policyAttributes.Any())
            return;

        // check if exists PolicyAllAttribute
        var policyAllAttribute = policyAttributes.OfType<PolicyAllAttribute>().FirstOrDefault();
        if (policyAllAttribute != null)
            return;

        // check if user is authenticated
        if (_authContext.IsAuthenticated)
        {
            // get current user and all roles
            var userTask = _userService.GetById(_authContext.UserId);
            var rolesTask = _roleService.GetAll();
            await Task.WhenAll(userTask, rolesTask);

            var user = userTask.Result;
            var roles = rolesTask.Result;

            if (user == null || !roles.Any())
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            // extract admin access roles
            var adminRole = roles.Where(r => r.IsAdmin).FirstOrDefault();
            if (adminRole == null)
                return;

            // check if user has admin access
            if (user.RoleIds.Contains(adminRole.Id))
                return;

            // check if user has access to the requested area and action
            foreach (var policyAttribute in policyAttributes)
            {
                var accessibleArea = roles.SelectMany(r => r.Policies).Where(p => p.Area == policyAttribute.Area).FirstOrDefault();
                if (accessibleArea == null)
                {
                    context.Result = new ForbidResult();
                    return;
                }

                if (!accessibleArea.Actions.Contains(policyAttribute.Action))
                {
                    context.Result = new ForbidResult();
                    return;
                }
            }
        }
        else
        {
            // not authenticated
            context.Result = new UnauthorizedResult();
            return;
        }

    }
}

