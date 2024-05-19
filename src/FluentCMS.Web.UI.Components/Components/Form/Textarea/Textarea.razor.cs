namespace FluentCMS.Web.UI.Components;

public partial class Textarea
{
    [Parameter]
    public int Rows { get; set; } = 5;

    public override string GetDefaultCSSName()
    {
        return "textarea";
    }
}
