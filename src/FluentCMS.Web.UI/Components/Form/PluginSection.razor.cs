namespace FluentCMS.Web.UI;

public partial class PluginSection
{
    [Parameter]
    public string? Title { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
