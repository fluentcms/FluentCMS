using Microsoft.AspNetCore.Authorization;

namespace FluentCMS.Web.Api;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public class ApiAuthAttribute() : Attribute
{
}

