namespace FluentCMS.Web.UI.Components;

public partial class Alert
{
    [Parameter]
    public bool Dismissible { get; set; }

    [Parameter]
    [CSSProperty]
    public AlertType Type { get; set; }

    private Task Close()
    {
        Visible = false;
        return Task.CompletedTask;
    }
}