namespace FluentCMS.Web.UI.Components;

public partial class Button : BaseComponentWithContent
{
    [Parameter]
    [CssProperty]
    public bool Block { get; set; }

    [Parameter]
    [CssProperty]
    public Color Color { get; set; } = Color.Default;

    [Parameter]
    [CssProperty]
    public bool Disabled { get; set; }

    [Parameter]
    [CssProperty]
    public bool Ghost { get; set; }

    [Parameter]
    public string? Href { get; set; }

    [Parameter]
    [CssProperty]
    public bool Link { get; set; }

    [Parameter]
    [CssProperty]
    public bool Outline { get; set; }

    [Parameter]
    [CssProperty]
    public ButtonSize Size { get; set; } = ButtonSize.Medium;

    [Parameter]
    public ButtonType Type { get; set; } = ButtonType.Button;
}
