using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FluentCMS.Web.Api.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public class AuthorizePolicyAttribute : TypeFilterAttribute
{
    public string Area { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;

    public AuthorizePolicyAttribute(string area, string action) : base(typeof(AuthorizePolicyRequirementFilter))
    {
        Area = area;
        Action = action;
    }
}

// https://code-maze.com/custom-authorize-attribute-aspnetcore/#:~:text=ASP.NET%20Core%20provides%20filters,invokes%20it%20is%20the%20IAuthorizationFilter%20.

public class AuthorizePolicyRequirementFilter : IAuthorizationFilter
{
    public AuthorizePolicyRequirementFilter(IAuthContext authContext)
    {
        var x = authContext;
    }

    public async void OnAuthorization(AuthorizationFilterContext context)
    {
        //var authorizationResult = await _authorizationService.AuthorizeAsync(context.HttpContext.User, _area, _action);
        //if (!authorizationResult.Succeeded)
        //{
        //    context.Result = new ForbidResult();
        //    // context.Result = new UnauthorizedObjectResult(string.Empty);
        //}
        await Task.CompletedTask;
    }
}
