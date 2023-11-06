using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace FluentCMS.Web.UI.Components;

// All components will be inherited from this class
public partial class BaseComponent : ComponentBase
{
    static int _counter = 0;
    public BaseComponent()
    {
        _counter++;
    }

    public const string DEFAULT_PREFIX = "f";

    [Parameter(CaptureUnmatchedValues = true)]
    public virtual Dictionary<string, object?> Attributes { get; set; } = [];

    [Parameter]
    public virtual string Class { get; set; } = string.Empty;

    [Parameter]
    public virtual string Tag { get; set; } = "div";

    [Parameter]
    public virtual string Prefix { get; set; } = DEFAULT_PREFIX;

    [Parameter]
    public virtual RenderFragment? ChildContent { get; set; }

    [Parameter]
    public ElementReference? Ref { get; set; }

    [Parameter]
    [Html]
    public virtual string? Id { get; set; }

    [Parameter]
    public virtual string? UniqueName { get; set; }

    [Parameter]
    public EventCallback<ElementReference> RefChanged { get; set; }

    protected override Task OnInitializedAsync()
    {
        if (string.IsNullOrEmpty(UniqueName))
            UniqueName = GetType().Name;

        if (string.IsNullOrEmpty(Id))
            Id = $"{UniqueName}-{_counter}";

        return base.OnInitializedAsync();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        //base.BuildRenderTree(builder);

        var _cssClasses = $"{Prefix}-{UniqueName?.PascalToKebabCase()} {GetCssClasses()}";
        if (!string.IsNullOrEmpty(Class))
            _cssClasses += $" {Class}";

        builder.OpenElement(0, Tag);

        if (Attributes?.Any() == true)
            builder.AddMultipleAttributes(1, Attributes);

        builder.AddMultipleAttributes(1, GetHtmlAttributes());

        builder.AddAttribute(2, "class", $"{_cssClasses}");

        if (Ref != null)
        {
            builder.AddElementReferenceCapture(3, async capturedRef =>
            {
                Ref = capturedRef;
                await RefChanged.InvokeAsync(Ref.Value);
            });
        }

        builder.AddContent(4, ChildContent);

        builder.CloseElement();
    }

    // This function will return an space separated string for generate CSS classes
    // It uses reflection to get all properties which have CssProperty attribute and their values
    private string GetCssClasses()
    {
        var _dict = GetCssProps();

        if (_dict == null || _dict.Count == 0)
            return string.Empty;

        var _cssClasses = string.Empty;

        var x = _dict
            .Where(x => !string.IsNullOrEmpty(x.Value))
            .Select(item => $"{Prefix}-{UniqueName?.PascalToKebabCase()}-{item.Key.PascalToKebabCase()}-{item.Value.PascalToKebabCase()}");

        return string.Join(" ", x);

    }

    // This is a function which returns a dictionary of property name and its value
    // It will return the properties with CssProperty attribute
    private Dictionary<string, string> GetCssProps()
    {
        var _dict = new Dictionary<string, string>();

        var properties = GetType().GetProperties().Where(prop => Attribute.IsDefined(prop, typeof(CssPropertyAttribute)));
        foreach (var property in properties)
        {
            var propertyName = property.Name;
            var propertyValue = property.GetValue(this) ?? string.Empty;

            _dict.Add(propertyName, propertyValue.ToString());
        }
        return _dict;
    }

    // This is a function which returns a dictionary of property name and its value
    // It will return the properties with CssProperty attribute
    private Dictionary<string, object> GetHtmlAttributes()
    {
        var _dict = new Dictionary<string, object>();

        var properties = GetType().GetProperties().Where(prop => Attribute.IsDefined(prop, typeof(HtmlAttribute)));
        foreach (var property in properties)
        {
            var propertyName = property.Name;
            var propertyValue = property.GetValue(this) ?? string.Empty;
            
            if (propertyValue is bool && (bool)propertyValue == false)
                continue;

            _dict.Add(propertyName.PascalToKebabCase(), propertyValue.ToString().PascalToKebabCase());
        }
        return _dict;
    }
}
