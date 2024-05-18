namespace FluentCMS.Web.UI.Plugins.Components;

public partial class PluginFormSelect<TValue>
{
    [Parameter]
    public int Cols { get; set; } = 12;

    private RenderFragment RenderBase() => (builder) => base.BuildRenderTree(builder);
}
