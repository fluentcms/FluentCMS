namespace FluentCMS.Web.UI.Components;

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

    public static string GetClasses(this BaseComponent baseComponent)
    {
        List<string> classes = new() { };

        // f-component
        classes.Add(string.Join(UISettings.SEPARATOR, [UISettings.PREFIX, baseComponent.ComponentName]));

        // add css properties
        classes = classes.Concat(ClassNames(baseComponent)).ToList();

        // add class property's value
        classes.Add(baseComponent.Class);

        return string.Join(" ", classes);
    }
}
