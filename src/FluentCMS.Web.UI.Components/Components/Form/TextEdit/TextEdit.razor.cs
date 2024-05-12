namespace FluentCMS.Web.UI.Components;

public partial class TextEdit
{
    #region BaseComponent

    [Parameter]
    public bool Visible { get; set; } = true;

    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;

    #endregion

    #region FormElements

    [Parameter]
    public bool Dense { get; set; }

    [Parameter]
    public string? Hint { get; set; }

    [Parameter]
    public string? Label { get; set; }

    [Parameter]
    public RenderFragment? LabelFragment { get; set; }

    #endregion

    #region  FormInputsType

    [Parameter]
    public IconName? IconEnd { get; set; }

    [Parameter]
    public IconName? IconStart { get; set; }

    #endregion

    #region Current

    [Parameter]
    [CSSProperty]
    public TextEditSize? Size { get; set; }

    #endregion
}
