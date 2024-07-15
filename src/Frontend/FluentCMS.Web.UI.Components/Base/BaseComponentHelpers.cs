namespace FluentCMS.Web.UI.Components;

public static class BaseComponentHelper
{

    // css prefix for auto-generated classes
    public const string CSS_PREFIX = "f";

    public const string SEPARATOR = "-";

    public static string ClassName(this ComponentBase baseComponent, string Name)
    {
        ArgumentNullException.ThrowIfNull(baseComponent);

        return string.Join(SEPARATOR, [CSS_PREFIX, Name.FromPascalCaseToKebabCase()]);
    }

    public static List<string> ClassNames(this IBaseComponent baseComponent)
    {
        var classes = new List<string>();

        // get properties with CSSProperty Attribute
        var properties = baseComponent.GetType().
            GetProperties().
            Where(p => p.CustomAttributes.Any(x => x.AttributeType == typeof(CSSPropertyAttribute)));

        var cssName = baseComponent.CSSName?.FromPascalCaseToKebabCase() ?? baseComponent.GetDefaultCSSName();

        foreach (var property in properties)
        {
            if (property.GetValue(baseComponent, null) is not { } value)
                continue;

            var propertyValue = value.ToString()?.FromPascalCaseToKebabCase() ?? string.Empty;
            classes.Add(string.Join(SEPARATOR, [CSS_PREFIX, cssName, property.Name.FromPascalCaseToKebabCase(), propertyValue]));
        }

        return classes;
    }

    public static string GetClasses(this IBaseComponent baseComponent)
    {
        var cssName = baseComponent.CSSName?.FromPascalCaseToKebabCase() ?? baseComponent.GetDefaultCSSName();

        // component's class name from its name (f-button, f-badge, etc.)
        var componentCss = string.Join(SEPARATOR, [CSS_PREFIX, cssName]);

        // add css properties
        List<string> classes = [componentCss, .. ClassNames(baseComponent)];

        // if class is set by user, add the same class name
        if (!string.IsNullOrEmpty(baseComponent.Class))
            classes = [.. classes, baseComponent.Class];

        return string.Join(" ", classes);
    }
}
