using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace FluentCMS.Web.Api.Attributes;
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class JwtAuthorize : AuthorizeAttribute
{
    public JwtAuthorize()
    {
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme;
    }
}
