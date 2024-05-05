namespace FluentCMS.Web.UI.Components;

public partial class PageHeader
{
    [Parameter]
    public bool HasBack { get; set; }

    [Parameter]
    public RenderFragment? PageHeaderActions { get; set; }

    [Parameter]
    public RenderFragment? PageHeaderSubtitle { get; set; }

    [Parameter]
    public string? SubTitle { get; set; }

    [Parameter]
    public string? Title { get; set; }
}
