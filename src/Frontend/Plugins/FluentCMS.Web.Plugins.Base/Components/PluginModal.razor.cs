namespace FluentCMS.Web.Plugins;

public partial class PluginModal
{
    [Parameter]
    public string? Title { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public bool Open { get; set; } = false;

    [Parameter]
    public EventCallback OnClose { get; set; }

    private async Task HandleClose()
    {
        await OnClose.InvokeAsync();
    }
}
