namespace FluentCMS.Web.UI.Components;

public static class BaseComponentHelper
{

    // css prefix for auto-generated classes
    public const string CSS_PREFIX = "f";

    public const string SEPARATOR = "-";

    public static string ClassName(this BaseComponent baseComponent, string Name)
    {
        ArgumentNullException.ThrowIfNull(baseComponent);

        return string.Join(SEPARATOR, [CSS_PREFIX, Name.FromPascalCaseToKebabCase()]);
    }

    public static List<string> ClassNames(this BaseComponent baseComponent,string cssName)
    {
        var classes = new List<string>();

        // get properties with CSSProperty Attribute
        var properties = baseComponent.GetType().GetProperties().Where(p => p.CustomAttributes.Any(x => x.AttributeType == typeof(CSSPropertyAttribute)));

        // var cssName = baseComponent.CSSName?.FromPascalCaseToKebabCase() ?? baseComponent.GetDefaultCSSName();

        foreach (var property in properties)
        {
            if (property.GetValue(baseComponent, null) is var value)
            {
                if (value != null)
                {
                    var propertyValue = value.ToString()?.FromPascalCaseToKebabCase() ?? string.Empty;
                    classes.Add(string.Join(SEPARATOR, [CSS_PREFIX, cssName, property.Name.FromPascalCaseToKebabCase(), propertyValue]));
                }
            }
        }

        return classes;
    }

    public static string GetClasses(this BaseComponent baseComponent, string nameOverride = "")
    {
        var cssName = string.IsNullOrEmpty(nameOverride) ? baseComponent.CSSName?.FromPascalCaseToKebabCase() ?? baseComponent.GetDefaultCSSName() : nameOverride.FromPascalCaseToKebabCase();

        // component's class name from its name (f-button, f-badge, etc.)
        var componentCss = string.Join(SEPARATOR, [CSS_PREFIX, cssName]);

        // add css properties
        List<string> classes = [componentCss, .. ClassNames(baseComponent, cssName)];

        // if class is set by user, add the same class name
        if (!string.IsNullOrEmpty(baseComponent.Class))
            classes = [.. classes, baseComponent.Class];

        return string.Join(" ", classes);
    }
}
