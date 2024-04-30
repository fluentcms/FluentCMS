using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace FluentCMS.Web.Api.Attributes;
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class JwtAuthorizeAttribute : AuthorizeAttribute//, IAuthorizationFilter
{
    public JwtAuthorizeAttribute()
    {
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme;
    }

    //public void OnAuthorization(AuthorizationFilterContext context)
    //{
    //    var httpContext = context.HttpContext;
    //    // Allow Anonymous skips all authorization
    //    //if (context.Filters.Any(item => item is IAllowAnonymousFilter))
    //    //{
    //    //    return;
    //    //}
    //    //string token = context.HttpContext.GetToken();
    //    //if (string.IsNullOrEmpty(token))
    //    //{
    //    //    context.HttpContext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
    //    //}
    //    //else
    //    //{
    //    //    string realmId = context.HttpContext.GetRealm();
    //    //    if (string.IsNullOrEmpty(realmId))
    //    //    {
    //    //        context.HttpContext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
    //    //    }
    //    //}
    //}
}
