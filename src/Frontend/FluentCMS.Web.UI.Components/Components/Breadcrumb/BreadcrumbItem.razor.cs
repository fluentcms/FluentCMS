namespace FluentCMS.Web.UI.Components;

public partial class BreadcrumbItem
{
    [Parameter]
    public string? Href { get; set; }

    [Parameter]
    public IconName IconName { get; set; } = IconName.Default;
}