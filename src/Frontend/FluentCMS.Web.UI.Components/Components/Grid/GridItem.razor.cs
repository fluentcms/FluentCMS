namespace FluentCMS.Web.UI.Components;

public partial class GridItem
{
    #region Column

    [Parameter]
    [CSSProperty]
    public GridItemColumn? Small { get; set; }

    [Parameter]
    [CSSProperty]
    public GridItemColumn? Medium { get; set; }

    [Parameter]
    [CSSProperty]
    public GridItemColumn? Large { get; set; }

    #endregion

    #region Hide

    [Parameter]
    [CSSProperty]
    public bool? HideSmall { get; set; }

    [Parameter]
    [CSSProperty]
    public bool? HideMedium { get; set; }

    [Parameter]
    [CSSProperty]
    public bool? HideLarge { get; set; }

    #endregion

    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;
}
