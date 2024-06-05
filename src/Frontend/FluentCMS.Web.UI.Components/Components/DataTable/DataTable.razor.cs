namespace FluentCMS.Web.UI.Components;

public partial class DataTable<TItem>
{
    [Parameter, EditorRequired]
    public IReadOnlyList<TItem>? Items { get; set; }

    [Parameter]
    public RenderFragment EmptyStateFragment { get; set; }
    
    [Parameter]
    public RenderFragment ColumnsFragment { get; set; }

    [Parameter]
    [CSSProperty]
    public bool Hoverable { get; set; } = true;
}
