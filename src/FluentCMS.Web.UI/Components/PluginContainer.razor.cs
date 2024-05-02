namespace FluentCMS.Web.UI.Plugins.Components;

public partial class PluginContainer
{
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [CascadingParameter]
    public PluginDetailResponse? Plugin { get; set; }
}
