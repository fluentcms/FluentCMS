namespace FluentCMS.Web.Plugins;

public partial class EmptyState
{
    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;

    [Parameter]
    public IconName Icon { get; set; } = IconName.Default;

    [Parameter]
    public string Message { get; set; } = "No results found!";
}
