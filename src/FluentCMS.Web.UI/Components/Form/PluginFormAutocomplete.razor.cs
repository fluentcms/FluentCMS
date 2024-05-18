namespace FluentCMS.Web.UI.Plugins.Components;

public partial class PluginFormAutocomplete
{
    [Parameter]
    public int Cols { get; set; } = 12;

    private RenderFragment RenderBase() => (builder) => base.BuildRenderTree(builder);
}
