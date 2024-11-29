namespace FluentCMS.Web.UI.Components;

public partial class Alert
{
    [Parameter]
    public bool Dismissible { get; set; }

    [Parameter]
    [CSSProperty]
    public AlertType Type { get; set; }

    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;

    private Task Close()
    {
        Visible = false;
        return Task.CompletedTask;
    }
}
