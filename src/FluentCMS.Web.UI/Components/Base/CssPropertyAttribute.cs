namespace FluentCMS.Web.UI.Components;

/// <summary>
/// This attribute is used to mark a property as a CSS property.
/// This is used to generate the CSS properties for the component.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class CssPropertyAttribute : Attribute
{
    public CssPropertyAttribute()
    {

    }
}
