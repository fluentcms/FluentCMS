namespace FluentCMS.Web.UI.Components;

public abstract class BaseComponent : ComponentBase
{
    // css prefix for auto-generated classes
    protected const string CSS_PREFIX = "f";
    protected const string SEPARATOR = "-";

    protected abstract RenderFragment BuildContent { get; }

    [Parameter]
    public bool Visible { get; set; } = true;

    [Parameter]
    public string? Class { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; } = default!;

    [Parameter]
    public string? CssName { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (Visible)
        {
            builder.AddContent(0, BuildContent);
        }
    }

    private string GetDefaultCssName()
    {
        if (!string.IsNullOrEmpty(CssName))
            return FromPascalCaseToKebabCase(CssName);

        var type = GetType();
        if (type.IsGenericType)
            return FromPascalCaseToKebabCase(type.Name.Split("`").First());
        else
            return FromPascalCaseToKebabCase(type.Name);
    }

    private static readonly ConcurrentDictionary<Type, CssPropertyMetadata[]> _cssPropertyCache = new();

    private sealed record CssPropertyMetadata(PropertyInfo Property, Func<BaseComponent, object?> ValueAccessor);

    private static CssPropertyMetadata[] ResolveCssPropertyMetadata(Type componentType)
    {
        return [.. componentType
            .GetProperties()
            .Where(p => p.CustomAttributes.Any(x => x.AttributeType == typeof(CssPropertyAttribute)))
            .Select(property =>
            {
                var componentParameter = Expression.Parameter(typeof(BaseComponent), "component");
                var castComponent = Expression.Convert(componentParameter, property.DeclaringType ?? componentType);
                var propertyAccess = Expression.Property(castComponent, property);
                var convertResult = Expression.Convert(propertyAccess, typeof(object));
                var lambda = Expression.Lambda<Func<BaseComponent, object?>>(convertResult, componentParameter).Compile();

                return new CssPropertyMetadata(property, lambda);
            })];
    }

    private List<string> ClassNames()
    {
        var classes = new List<string>();

        // get properties with CSSProperty Attribute
        var componentType = GetType();
        var properties = _cssPropertyCache.GetOrAdd(componentType, ResolveCssPropertyMetadata);

        var cssName = GetDefaultCssName();

        foreach (var property in properties)
        {
            var value = property.ValueAccessor(this);
            if (value is null)
                continue;

            var propertyValueString = value.ToString() ?? string.Empty;
            var propertyValue = FromPascalCaseToKebabCase(propertyValueString) ?? string.Empty;
            classes.Add(string.Join(SEPARATOR, [CSS_PREFIX, cssName, FromPascalCaseToKebabCase(property.Property.Name), propertyValue]));
        }

        return classes;
    }

    public virtual string GetClasses()
    {
        var cssName = GetDefaultCssName();

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
