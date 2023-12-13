namespace FluentCMS.Web.UI.Components.Core;

public static class BaseComponentHelper
{
    public static string ClassName(this BaseComponent baseComponent, string Name)
    {
        return string.Join(UISettings.SEPARATOR, [UISettings.PREFIX, Name.FromPascalCaseToKebabCase()]);
    }

    public static List<string> ClassNames(this BaseComponent baseComponent)
    {
        var classes = new List<string>();

        // get properties
        var properties = baseComponent.GetType().
            GetProperties().
            Where(p => p.CustomAttributes.Any(x => x.AttributeType == typeof(CssPropertyAttribute)));

        foreach (var property in properties)
        {
            if (property.GetValue(baseComponent, null) is var value)
            {
                if (value != null)
                {
                    var propertyValue = value.ToString()?.FromPascalCaseToKebabCase() ?? string.Empty;
                    classes.Add(string.Join(UISettings.SEPARATOR, [UISettings.PREFIX, baseComponent.ComponentName, property.Name.FromPascalCaseToKebabCase(), propertyValue]));
                }
            }
        }

        return classes;
    }

    // Get Classes
    public static string GetClasses(this BaseComponent baseComponent)
    {
        var classes = ClassNames(baseComponent);

        // add component name
        classes.Add(string.Join(UISettings.SEPARATOR, [UISettings.PREFIX, baseComponent.ComponentName]));

        return string.Join(" ", classes);
    }
}