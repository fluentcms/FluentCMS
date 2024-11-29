namespace FluentCMS.Web.UI.Components;

public partial class DataTable<TItem>
{
    [Parameter]
    public EventCallback<TItem> OnRowClick { get; set; }

    [Parameter, EditorRequired]
    public IReadOnlyList<TItem>? Items { get; set; }

    [Parameter]
    [CSSProperty]
    public bool Hoverable { get; set; } = true;

    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;

    private async Task HandleRowClick(TItem item)
    {
        await OnRowClick.InvokeAsync(item);
    }
}
