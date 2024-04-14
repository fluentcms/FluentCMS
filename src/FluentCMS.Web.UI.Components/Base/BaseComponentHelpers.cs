namespace FluentCMS.Web.UI.Components;

public static class BaseComponentHelper
{

    // css prefix for auto-generated classes
    public const string PREFIX = "f";

    public const string SEPARATOR = "-";

    public static string ClassName(this BaseComponent baseComponent, string Name)
    {
        ArgumentNullException.ThrowIfNull(baseComponent);

        return string.Join(SEPARATOR, [PREFIX, Name.FromPascalCaseToKebabCase()]);
    }

    public static List<string> ClassNames(this BaseComponent baseComponent)
    {
        var classes = new List<string>();

        // get properties with CSSProperty Attribute
        var properties = baseComponent.GetType().
            GetProperties().
            Where(p => p.CustomAttributes.Any(x => x.AttributeType == typeof(CSSPropertyAttribute)));

        foreach (var property in properties)
        {
            if (property.GetValue(baseComponent, null) is var value)
            {
                if (value != null)
                {
                    var propertyValue = value.ToString()?.FromPascalCaseToKebabCase() ?? string.Empty;
                    classes.Add(string.Join(SEPARATOR, [PREFIX, baseComponent.ComponentName, property.Name.FromPascalCaseToKebabCase(), propertyValue]));
                }
            }
        }

        return classes;
    }

    public static string GetClasses(this BaseComponent baseComponent)
    {
        // component's class name from its name (f-button, f-badge, etc.)
        var componentCss = string.Join(SEPARATOR, [PREFIX, baseComponent.ComponentName]);

        // add css properties
        List<string> classes = [componentCss, .. ClassNames(baseComponent), baseComponent.Class];

        return string.Join(" ", classes);
    }
}
