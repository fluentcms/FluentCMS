using Microsoft.AspNetCore.Authorization;

namespace FluentCMS.Web.Api;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public class PolicyAttribute(string area, string action) : Attribute
{
    public string Area { get; set; } = area;
    public string Action { get; set; } = action;
}
