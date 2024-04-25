namespace FluentCMS.Web.UI.Plugins;

public partial class PluginView
{
    [Parameter]
    public string? Title { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [CascadingParameter]
    public ErrorContext ErrorContext { get; set; } = default!;
}
