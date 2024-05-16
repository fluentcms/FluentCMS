namespace FluentCMS.Web.UI.Components;

public partial class TextInput
{
    [Parameter]
    [CSSProperty]
    public InputSize? Size { get; set; }

    [Parameter]
    public TextInputType Type { get; set; } = TextInputType.Text;

}
