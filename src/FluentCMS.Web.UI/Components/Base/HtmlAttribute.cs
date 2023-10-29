namespace FluentCMS.Web.UI.Components;

/// <summary>
/// This attribute is used to mark a property being rendered as HTML attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class HtmlAttribute : Attribute
{
    public HtmlAttribute()
    {

    }
}
