namespace FluentCMS.Web.UI.Components;

public partial class FormField
{
    [Parameter]
    public int Cols { get; set; } = 12;

    [Parameter]
    public bool Dense { get; set; }

    [Parameter]
    public string Id { get; set; }

    [Parameter]
    public string? Label { get; set; }

    [Parameter]
    public RenderFragment? LabelFragment { get; set; }

    [Parameter]
    public bool Required { get; set; }

    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;
}
