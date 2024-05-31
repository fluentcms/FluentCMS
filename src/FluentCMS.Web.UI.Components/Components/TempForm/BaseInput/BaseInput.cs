namespace FluentCMS.Web.UI.Plugins.Components;

public abstract class BaseInput<T> : InputBase<T>
{
    [Parameter]
    [CSSProperty]
    public bool Disabled { get; set; }

    [Parameter]
    public bool Dense { get; set; }

    [Parameter]
    public string? Id { get; set; }

    [Parameter]
    public string? Label { get; set; }

    [Parameter]
    public RenderFragment? LabelFragment { get; set; }

    [Parameter]
    public string? Name { get; set; }

    [Parameter]
    public bool Readonly { get; set; }

    [Parameter]
    public bool Required { get; set; }

    public BaseInput()
    {
        Id ??= Guid.NewGuid().ToString();
    }

    [Parameter]
    public bool Visible { get; set; } = true;

    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;

    [Parameter]
    public string? Class { get; set; }

    [Parameter]
    public string? CSSName { get; set; }

    public virtual string GetDefaultCSSName()
    {
        var type = GetType();
        if (type.IsGenericType)
            return type.Name.Split("`").First().FromPascalCaseToKebabCase();
        else
            return type.Name.FromPascalCaseToKebabCase();
    }

}

public abstract class BaseInputInline<T> : BaseInput<T>
{
}

public abstract class BaseInputBlock<T> : BaseInput<T>
{
    [Parameter]
    public string? Placeholder { get; set; }
}

public class BaseInputHelper
{

    // css prefix for auto-generated classes
    public const string CSS_PREFIX = "f";

    public const string SEPARATOR = "-";

    public static string ClassName<T>(BaseInput<T> baseInput, string Name)
    {
        ArgumentNullException.ThrowIfNull(baseInput);

        return string.Join(SEPARATOR, [CSS_PREFIX, Name.FromPascalCaseToKebabCase()]);
    }

    public static List<string> ClassNames<T>(BaseInput<T> baseInput)
    {
        var classes = new List<string>();

        // get properties with CSSProperty Attribute
        var properties = baseInput.GetType().
            GetProperties().
            Where(p => p.CustomAttributes.Any(x => x.AttributeType == typeof(CSSPropertyAttribute)));

        var cssName = baseInput.CSSName?.FromPascalCaseToKebabCase() ?? baseInput.GetDefaultCSSName();

        foreach (var property in properties)
        {
            if (property.GetValue(baseInput, null) is var value)
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

    public static string GetClasses<T>(BaseInput<T> baseInput)
    {
        var cssName = baseInput.CSSName?.FromPascalCaseToKebabCase() ?? baseInput.GetDefaultCSSName();

        // component's class name from its name (f-button, f-badge, etc.)
        var componentCss = string.Join(SEPARATOR, [CSS_PREFIX, cssName]);

        // add css properties
        List<string> classes = [componentCss, .. ClassNames(baseInput)];

        // if class is set by user, add the same class name
        if (!string.IsNullOrEmpty(baseInput.Class))
            classes = [.. classes, baseInput.Class];

        return string.Join(" ", classes);
    }
}