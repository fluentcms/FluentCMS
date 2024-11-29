namespace FluentCMS.Web.Plugins;

public partial class ActionButton
{
    [Parameter]
    public IconName IconName { get; set; } = IconName.Default;

    [Parameter]
    public Color Color { get; set; } = Color.Default;

    [Parameter]
    public string Href { get; set; } = string.Empty;

    [Parameter]
    public string Label { get; set; } = string.Empty;

    [CascadingParameter]
    public bool TableAction { get; set; }
}