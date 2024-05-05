namespace FluentCMS.Web.UI.Components;

public partial class PaginationItem
{
    [Parameter]
    [CSSProperty]
    public bool Active { get; set; }

    [Parameter]
    [CSSProperty]
    public bool Disabled { get; set; }

    [Parameter]
    public EventCallback<MouseEventArgs> OnClick { get; set; }
}