namespace FluentCMS.Web.UI;

public partial class PageToolbar
{
    [Parameter]
    public RenderFragment Start { get; set; } = default!;

    [Parameter]
    public RenderFragment Center { get; set; } = default!;

    [Parameter]
    public RenderFragment End { get; set; } = default!;
}