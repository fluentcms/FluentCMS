namespace FluentCMS.Web.UI.Components;

public partial class StepperItem
{
    [Parameter]
    [CSSProperty]
    public Color Color { get; set; } = Color.Default;

    [Parameter]
    public IconName Icon { get; set; } = IconName.Default;

    [Parameter]
    public string? Subtitle { get; set; }

    [Parameter]
    public string? Title { get; set; }
}
