namespace FluentCMS.Web.Api.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public class DynamicAuthorizeAttribute : Attribute
{
    public string Area { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;

    public DynamicAuthorizeAttribute(string area, string action)
    {
        Area = area;
        Action = action;
    }
}
