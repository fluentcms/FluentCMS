namespace FluentCMS.Web.UI.Components;

public abstract class BaseInput<T> : InputBase<T>, IBaseComponent
{
    #region IBaseComponent

    [Parameter]
    public bool Visible { get; set; } = true;

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

    #endregion

    #region Fields

    [Parameter]
    [CSSProperty]
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

}

public abstract class BaseInputInline<T> : BaseInput<T>
{
}

public abstract class BaseInputBlock<T> : BaseInput<T>
{
    [Parameter]
    public string? Placeholder { get; set; }
}
