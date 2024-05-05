namespace FluentCMS.Web.UI.Components;

public partial class Select
{
    [Parameter]
    public bool Multiple { get; set; }

    [Parameter]
    [CSSProperty]
    public InputSize? Size { get; set; }
}
