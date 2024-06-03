namespace FluentCMS.Web.UI.Plugins;

public partial class PluginBody
{
    [Parameter]
    public RenderFragment? ActionsFragment { get; set; }

    [Parameter]
    public string? Title { get; set; }

    [Parameter]
    public bool Visible { get; set; } = true;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
