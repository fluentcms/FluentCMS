namespace FluentCMS.Web.UI.Components;

public partial class TabItem
{
    [CSSProperty]
    public bool Active => Name == Parent?.Value;

    [Parameter]
    public string Name { get; set; } = default!;

    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;

    [CascadingParameter]
    public Tabs? Parent { get; set; }

    private async Task OnClicked()
    {
        Parent?.Activate(Name);
    }
}
