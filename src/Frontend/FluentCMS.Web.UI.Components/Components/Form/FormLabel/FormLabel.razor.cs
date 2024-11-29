namespace FluentCMS.Web.UI.Components;

public partial class FormLabel
{
    [Parameter]
    public string? Id { get; set; }

    [Parameter]
    public bool Required { get; set; }

    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;
}
