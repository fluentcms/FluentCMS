namespace FluentCMS.Web.Plugins;

public partial class ActionButtons<TItem>
{
    [Parameter]
    public string Label { get; set; } = default!;

    [Parameter, EditorRequired]
    public RenderFragment<TItem> ChildContent { get; set; } = default!;

    [CascadingParameter]
    public TItem Item { get; set; } = default!;

    [CascadingParameter(Name = "DataTableColumnHeader")]
    public bool Header { get; set; }

    public bool TableAction = true;
}
