namespace FluentCMS.Web.UI.ComponentsDocs.Components;

public partial class Sidebar
{
    [Parameter]
    public RenderFragment? Header { get; set; }

    [Parameter]
    public string? Id { get; set; }

    [Parameter]
    public RenderFragment? Footer { get; set; }

    [Parameter]
    [CSSProperty]
    public bool Secondary { get; set; }

    [Parameter]
    public string? Title { get; set; }
}
