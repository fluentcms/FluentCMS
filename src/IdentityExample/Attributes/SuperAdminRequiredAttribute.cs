using IdentityExample.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IdentityExample.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class SuperAdminRequiredAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

        if (!currentUser.IsAuthenticated)
        {
            context.Result = new UnauthorizedObjectResult(new ApiResponse
            {
                Success = false,
                Message = "Authentication required",
                StatusCode = 401,
                Timestamp = DateTime.UtcNow,
                TraceId = context.HttpContext.TraceIdentifier
            });
            return;
        }

        if (!currentUser.IsSuperAdmin)
        {
            context.Result = new ObjectResult(new ApiResponse
            {
                Success = false,
                Message = "SuperAdmin access required",
                StatusCode = 403,
                Timestamp = DateTime.UtcNow,
                TraceId = context.HttpContext.TraceIdentifier
            })
            {
                StatusCode = 403
            };
            return;
        }
    }
}
