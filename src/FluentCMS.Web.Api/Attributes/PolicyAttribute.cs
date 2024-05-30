namespace FluentCMS.Web.Api;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public class PolicyAttribute(string area, string action) : Attribute
{
    public string Area { get; set; } = area;
    public string Action { get; set; } = action;
}

public class PolicyAllAttribute : PolicyAttribute
{
    public const string AREA = "Global";
    public const string ACTION = "All";

    public PolicyAllAttribute() : base(AREA, ACTION)
    {
    }
}
