namespace FluentCMS.Web.UI;

public partial class PluginContainerActions
{
    [Parameter]
    public PluginViewState Plugin { get; set; } = default!;

    [Parameter]
    public EventCallback OnDelete { get; set; } = default!;

    [Inject]
    public ViewState ViewState { get; set; } = default!;

    private bool IsDesignMode { get; set; } = false;

    private async Task HandleDelete()
    {
        await OnDelete.InvokeAsync();   
    }

    protected override async Task OnInitializedAsync()
    {
        IsDesignMode = ViewState.Type == ViewStateType.PagePreview;
        await Task.CompletedTask;
    }
}
