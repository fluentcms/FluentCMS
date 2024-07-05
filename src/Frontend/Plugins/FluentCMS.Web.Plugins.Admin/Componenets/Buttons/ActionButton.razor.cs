using FluentCMS.Web.UI.Components;

namespace FluentCMS.Web.Plugins;

public partial class ActionButton
{
    [Parameter]
    public IconName IconName { get; set; } = IconName.Default;

    [Parameter]
    public Color Color { get; set; } = Color.Default;

    [Parameter]
    public string Href { get; set; }

    [Parameter]
    public string Label { get; set; }

    [CascadingParameter]
    public bool TableAction { get; set; }
}