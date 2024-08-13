using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Web.Api.Filters;

public class PolicyAuthorizeFilter : IAsyncAuthorizationFilter
{
    const string _header = "X-API-AUTH";

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

        var authContext = context.HttpContext.RequestServices.GetRequiredService<IAuthContext>();
        var userService = context.HttpContext.RequestServices.GetRequiredService<IUserService>();
        var roleService = context.HttpContext.RequestServices.GetRequiredService<IRoleService>();
        var apiTokenService = context.HttpContext.RequestServices.GetRequiredService<IApiTokenService>();

        // Check if the required header is present
        if (!context.HttpContext.Request.Headers.TryGetValue(_header, out var extractedApiHeader))
        {
            context.Result = new ForbidResult(); // Reject the request
            return;
        }

        var combinedKeySecret = extractedApiHeader.First();
        if (string.IsNullOrEmpty(combinedKeySecret))
        {
            context.Result = new ForbidResult(); // Reject the request
            return;
        }

        var parts = combinedKeySecret.Split(':');
        if (parts.Length != 2)
        {
            context.Result = new ForbidResult(); // Reject the request
            return;
        }

        var apiKey = parts[0];
        var providedSecret = parts[1];

        // Check if the api key is valid
        var isValid = await apiTokenService.Validate(apiKey, providedSecret);

        if (isValid is null)
        {
            context.Result = new ForbidResult(); // Reject the request
            return;
        }

        // check if user is authenticated
        if (authContext.IsAuthenticated)
        {
            // get current user and all roles
            var userTask = userService.GetById(authContext.UserId);
            var rolesTask = roleService.GetAll();
            await Task.WhenAll(userTask, rolesTask);

            var user = userTask.Result;
            var roles = rolesTask.Result;

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

        }
        else
        {
            // not authenticated
            context.Result = new UnauthorizedResult();
            return;
        }
    }
}

