namespace FluentCMS.Web.UI.Components;

public interface IBaseComponent
{
    bool Visible { get; set; }
    string? Class { get; set; }
    string? CssName { get; set; }
    IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }
    string GetDefaultCssName();
}


public abstract class BaseComponent : ComponentBase, IBaseComponent
{
    [Parameter]
    public bool Visible { get; set; } = true;

    [Parameter]
    public string? Class { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; } = default!;

    [Parameter]
    public string? CssName { get; set; }

    protected string CssClass => BaseComponentHelper.GetClasses(this);

    protected IReadOnlyList<string> CssClassList => BaseComponentHelper.ClassNames(this);

    public virtual string GetDefaultCssName()
    {
        var type = GetType();
        if (type.IsGenericType)
            return type.Name.Split("`").First().FromPascalCaseToKebabCase();
        else
            return type.Name.FromPascalCaseToKebabCase();
    }
}
