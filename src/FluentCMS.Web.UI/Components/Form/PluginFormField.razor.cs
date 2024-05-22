namespace FluentCMS.Web.UI.Plugins.Components;

public partial class PluginFormField
{
    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;

    [Parameter]
    public int Cols { get; set; } = 12;

    [Parameter]
    public bool Visible { get; set; } = true;
}
