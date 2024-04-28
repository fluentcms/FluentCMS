namespace FluentCMS.Web.UI.Plugins;

public partial class PluginView
{
    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
