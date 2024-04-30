using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FluentCMS.Web.Api.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public class PolicyAttribute : Attribute, IAuthorizationFilter //: AuthorizeAttribute
{
    public string Area { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;

    public PolicyAttribute(string area, string action) //: base(typeof(PolicyAttribute))
    {
        //AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme;
        Area = area;
        Action = action;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        //throw new NotImplementedException();
    }
}

// https://code-maze.com/custom-authorize-attribute-aspnetcore/#:~:text=ASP.NET%20Core%20provides%20filters,invokes%20it%20is%20the%20IAuthorizationFilter%20.
// https://weblog.west-wind.com/posts/2022/Mar/29/Combining-Bearer-Token-and-Cookie-Auth-in-ASPNET

//public class PolicyRequirementFilter : IAuthorizationFilter
//{
//    public PolicyRequirementFilter(IAuthContext authContext)
//    {
//        var x = authContext;
//    }

//    public async void OnAuthorization(AuthorizationFilterContext context)
//    {
//        //var authorizationResult = await _authorizationService.AuthorizeAsync(context.HttpContext.User, _area, _action);
//        //if (!authorizationResult.Succeeded)
//        //{
//        //    context.Result = new ForbidResult();
//        //    // context.Result = new UnauthorizedObjectResult(string.Empty);
//        //}
//        await Task.CompletedTask;
//    }
//}

//public class PolicyRequirement : AuthorizationHandler<PolicyAttribute>
//{
//    public PolicyRequirement()
//    {

//    }
//    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PolicyAttribute requirement)
//    {
//        var httpContext = context.User;
//        return Task.CompletedTask;
//    }

//    public override async Task HandleAsync(AuthorizationHandlerContext context)
//    {
//        //await HandleRequirementAsync(context, req).ConfigureAwait(false);
//        await base.HandleAsync(context);
//    }
//}
