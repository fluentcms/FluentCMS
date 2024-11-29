namespace FluentCMS.Web.UI.Components;

public partial class Grid
{
    #region Items

    [Parameter]
    [CSSProperty]
    public GridItems? ItemsSmall { get; set; }

    [Parameter]
    [CSSProperty]
    public GridItems? ItemsMedium { get; set; }

    [Parameter]
    [CSSProperty]
    public GridItems? ItemsLarge { get; set; }

    #endregion

    #region Gutter

    [Parameter]
    [CSSProperty]
    public GridGutter? Gutter { get; set; }

    [Parameter]
    [CSSProperty]
    public GridGutter? GutterX { get; set; }

    [Parameter]
    [CSSProperty]
    public GridGutter? GutterY { get; set; }

    #endregion

    #region Justify

    [Parameter]
    [CSSProperty]
    public GridJustify? JustifySmall { get; set; }

    [Parameter]
    [CSSProperty]
    public GridJustify? JustifyMedium { get; set; }

    [Parameter]
    [CSSProperty]
    public GridJustify? JustifyLarge { get; set; }

    #endregion

    [Parameter]
    [CSSProperty]
    public bool NoWrap { get; set; }

    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;
}
