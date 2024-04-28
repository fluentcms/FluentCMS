namespace FluentCMS.Web.Api.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public class AuthorizePolicyAttribute : Attribute
{
    public string Area { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;

    public AuthorizePolicyAttribute(string area, string action)
    {
        Area = area;
        Action = action;
    }
}
