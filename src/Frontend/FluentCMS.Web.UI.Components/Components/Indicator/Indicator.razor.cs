namespace FluentCMS.Web.UI.Components;

public partial class Indicator
{
    [Parameter]
    [CSSProperty]
    public Color Color { get; set; } = Color.Default;
}
