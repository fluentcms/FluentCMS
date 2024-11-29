namespace FluentCMS.Web.UI.Components;

public abstract class BaseComponent : ComponentBase, IBaseComponent
{
    [Parameter]
    public bool Visible { get; set; } = true;

    [Parameter]
    public string? Class { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; } = default!;

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
