namespace FluentCMS.Web.UI.Components;

public partial class BreadcrumbItem
{
    [Parameter]
    public string? Href { get; set; }

    [Parameter]
    public bool Link { get; set; } = false;

    [Parameter]
    public IconName IconName { get; set; } = IconName.Default;

    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;
}
