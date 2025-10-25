using System.Collections.Concurrent;
using System.Reflection;

namespace FluentCMS.Web.UI.Components;

public static class BaseComponentHelper
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> CssPropertyCache = new();

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
        var componentType = baseComponent.GetType();
        var properties = CssPropertyCache.GetOrAdd(componentType, type => type.
            GetProperties().
            Where(p => p.CustomAttributes.Any(x => x.AttributeType == typeof(CSSPropertyAttribute))).
            ToArray());

        var cssName = baseComponent.CssName?.FromPascalCaseToKebabCase() ?? baseComponent.GetDefaultCssName();

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
        var cssName = baseComponent.CssName?.FromPascalCaseToKebabCase() ?? baseComponent.GetDefaultCssName();

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
