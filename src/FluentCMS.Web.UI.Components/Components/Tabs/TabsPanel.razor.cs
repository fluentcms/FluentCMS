namespace FluentCMS.Web.UI.Components

public partial class TabsPanel
{
    [CSSProperty]
    public bool Active => Name == Parent?.Value;

    [Parameter]
    public string Name { get; set; } = default!;

    [CascadingParameter]
    public Tabs? Parent { get; set; }
}


