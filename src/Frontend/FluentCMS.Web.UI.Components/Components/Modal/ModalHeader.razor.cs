namespace FluentCMS.Web.UI.Components;

public partial class ModalHeader
{
    [Parameter]
    public bool Closable { get; set; } = true;

    [CascadingParameter]
    public Modal Modal { get; set; } = default!;

    [Parameter]
    public string? Title { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}

