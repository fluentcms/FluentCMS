namespace FluentCMS.Web.UI.Components;

public abstract partial class BaseComponent : ComponentBase, IBaseComponent
{
    // css prefix for auto-generated classes
    public const string CSS_PREFIX = "f";
    public const string SEPARATOR = "-";

    protected abstract RenderFragment BuildContent { get; }

    [Parameter]
    public bool Visible { get; set; } = true;

    [Parameter]
    public string? Class { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; } = default!;

    [Parameter]
    public string? CssName { get; set; }

    private string GetDefaultCssName()
    {
        var type = GetType();
        if (type.IsGenericType)
            return FromPascalCaseToKebabCase(type.Name.Split("`").First());
        else
            return FromPascalCaseToKebabCase(type.Name);
    }

    private static readonly ConcurrentDictionary<Type, CssPropertyMetadata[]> _cssPropertyCache = new();

    private sealed record CssPropertyMetadata(PropertyInfo Property, Func<IBaseComponent, object?> ValueAccessor);

    private static CssPropertyMetadata[] ResolveCssPropertyMetadata(Type componentType)
    {
        return [.. componentType
            .GetProperties()
            .Where(p => p.CustomAttributes.Any(x => x.AttributeType == typeof(CssPropertyAttribute)))
            .Select(property =>
            {
                var componentParameter = Expression.Parameter(typeof(IBaseComponent), "component");
                var castComponent = Expression.Convert(componentParameter, property.DeclaringType ?? componentType);
                var propertyAccess = Expression.Property(castComponent, property);
                var convertResult = Expression.Convert(propertyAccess, typeof(object));
                var lambda = Expression.Lambda<Func<IBaseComponent, object?>>(convertResult, componentParameter).Compile();

                return new CssPropertyMetadata(property, lambda);
            })];
    }

    private List<string> ClassNames()
    {
        var classes = new List<string>();

        // get properties with CSSProperty Attribute
        var componentType = GetType();
        var properties = _cssPropertyCache.GetOrAdd(componentType, ResolveCssPropertyMetadata);

        var cssName = string.IsNullOrEmpty(CssName) ? GetDefaultCssName() : FromPascalCaseToKebabCase(CssName);

        foreach (var property in properties)
        {
            if (property.ValueAccessor(this) is not { } value)
                continue;

            var propertyValueString = value.ToString() ?? string.Empty;
            var propertyValue = FromPascalCaseToKebabCase(propertyValueString) ?? string.Empty;
            classes.Add(string.Join(SEPARATOR, [CSS_PREFIX, cssName, FromPascalCaseToKebabCase(property.Property.Name), propertyValue]));
        }

        return classes;
    }

    public virtual string GetClasses()
    {
        var cssName = string.IsNullOrEmpty(CssName) ? GetDefaultCssName() : FromPascalCaseToKebabCase(CssName);

        // component's class name from its name (f-button, f-badge, etc.)
        var componentCss = string.Join(SEPARATOR, [CSS_PREFIX, cssName]);

        // add css properties
        List<string> classes = [componentCss, .. ClassNames()];

        // if class is set by user, add the same class name
        if (!string.IsNullOrEmpty(Class))
            classes = [.. classes, Class];

        return string.Join(" ", classes);
    }

    // from PascalCase to kebab-case
    protected static string FromPascalCaseToKebabCase(string value)
    {
        return string.Concat(value.Select((x, i) => i > 0 && char.IsUpper(x) ? "-" + x : x.ToString())).ToLower();
    }
}
