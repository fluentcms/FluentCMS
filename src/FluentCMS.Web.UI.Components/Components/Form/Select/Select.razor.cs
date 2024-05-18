namespace FluentCMS.Web.UI.Components;

public partial class Select<TValue>
{
    [Parameter]
    public bool Multiple { get; set; }

    [Parameter]
    [CSSProperty]
    public InputSize? Size { get; set; }

    public override string GetDefaultCSSName()
    {
        return "Select";
    }
}
