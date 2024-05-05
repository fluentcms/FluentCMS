namespace FluentCMS.Web.UI.Components;

public partial class SidebarItem
{
    [Parameter]
    [CSSProperty]
    public bool Active { get; set; }

    [Parameter]
    public string? Href { get; set; }

    [Parameter]
    public IconName Icon { get; set; }

    [Parameter]
    public string? Target { get; set; }
}
