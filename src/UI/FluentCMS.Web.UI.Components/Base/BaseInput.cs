namespace FluentCMS.Web.UI.Components;

public abstract class BaseInput<T> : InputBase<T>
{
    protected const string CSS_PREFIX = "f";
    protected const string SEPARATOR = "-";

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public string? Class { get; set; }

    #region Fields

    [Parameter, CssProperty]
    public bool Disabled { get; set; }

    [Parameter]
    public bool Dense { get; set; }

    [Parameter]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Parameter]
    public string? Label { get; set; }

    [Parameter]
    public string? Hint { get; set; }

    [Parameter]
    public RenderFragment? LabelFragment { get; set; }

    [Parameter]
    public string? Name { get; set; }

    [Parameter]
    public bool Readonly { get; set; }

    [Parameter]
    public bool Required { get; set; }

    #endregion

    [Parameter]
    public string? CssName { get; set; }

    protected string ClassName(string name)
    {
        return string.Join(SEPARATOR, CSS_PREFIX, FromPascalCaseToKebabCase(name));
    }

    private string GetDefaultCssName()
    {
        if (!string.IsNullOrEmpty(CssName))
            return FromPascalCaseToKebabCase(CssName);

        var typeName = GetType().Name;
        if (typeName.Contains('`'))
            typeName = typeName[..typeName.IndexOf('`')];

        return FromPascalCaseToKebabCase(typeName);
    }

    private static readonly ConcurrentDictionary<Type, CssPropertyMetadata[]> _cssPropertyCache = new();

    private sealed record CssPropertyMetadata(PropertyInfo Property, Func<BaseInput<T>, object?> ValueAccessor);

    private static CssPropertyMetadata[] ResolveCssPropertyMetadata(Type componentType)
    {
        return componentType
            .GetProperties()
            .Where(p => p.CustomAttributes.Any(a => a.AttributeType == typeof(CssPropertyAttribute)))
            .Select(property =>
            {
                var parameter = Expression.Parameter(typeof(BaseInput<T>), "component");
                var cast = Expression.Convert(parameter, property.DeclaringType ?? componentType);
                var propertyAccess = Expression.Property(cast, property);
                var convert = Expression.Convert(propertyAccess, typeof(object));
                var lambda = Expression.Lambda<Func<BaseInput<T>, object?>>(convert, parameter).Compile();
                return new CssPropertyMetadata(property, lambda);
            }).ToArray();
    }

    private List<string> ClassNames()
    {
        var componentType = GetType();
        var properties = _cssPropertyCache.GetOrAdd(componentType, ResolveCssPropertyMetadata);
        var cssName = GetDefaultCssName();
        var classes = new List<string>();

        foreach (var property in properties)
        {
            var value = property.ValueAccessor(this);
            if (value is null || value is bool b && !b)
                continue;

            if (value is true)
            {
                classes.Add($"{CSS_PREFIX}{SEPARATOR}{cssName}{SEPARATOR}{FromPascalCaseToKebabCase(property.Property.Name)}");
            }
            else
            {
                var val = FromPascalCaseToKebabCase(value.ToString() ?? string.Empty);
                classes.Add($"{CSS_PREFIX}{SEPARATOR}{cssName}{SEPARATOR}{FromPascalCaseToKebabCase(property.Property.Name)}{SEPARATOR}{val}");
            }
        }

        return classes;
    }

    public virtual string GetClasses()
    {
        var cssName = GetDefaultCssName();
        var componentCss = $"{CSS_PREFIX}{SEPARATOR}{cssName}";
        var classes = new List<string> { componentCss };
        classes.AddRange(ClassNames());

        if (!string.IsNullOrEmpty(Class))
            classes.Add(Class);

        return string.Join(" ", classes);
    }

    protected static string FromPascalCaseToKebabCase(string value)
    {
        return string.Concat(value.Select((x, i) => i > 0 && char.IsUpper(x) ? "-" + x : x.ToString())).ToLower();
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
