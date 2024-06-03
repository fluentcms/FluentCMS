﻿using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Web.Api.Filters;

public class PolicyAuthorizeFiler : IAsyncAuthorizationFilter
{
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

        // check if user is authenticated
        if (authContext.IsAuthenticated)
        {
            // get current user and all roles
            var userTask = userService.GetById(authContext.UserId);
            var rolesTask = roleService.GetAll();
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

