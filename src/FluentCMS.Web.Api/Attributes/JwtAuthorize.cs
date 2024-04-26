using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Web.Api.Attributes;
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class JwtAuthorize : AuthorizeAttribute
{
    public JwtAuthorize()
    {
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme;
    }
}
