namespace FluentCMS.Web.UI.Plugins.Components;

public partial class PluginFormField
{
    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;

    [Parameter]
    public int Cols { get; set; } = 12;

    [Parameter]
    public string Id { get; set; }

    [Parameter]
    public string? Label { get; set; }

    [Parameter]
    public RenderFragment? LabelFragment { get; set; }

    [Parameter]
    public bool Required { get; set; }

    [Parameter]
    public bool Visible { get; set; } = true;
}
