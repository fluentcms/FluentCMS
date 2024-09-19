namespace FluentCMS.Web.UI;

public partial class PluginContainerActions
{
    [Parameter]
    public PluginViewState Plugin { get; set; } = default!;

    [Inject]
    public ViewState ViewState { get; set; } = default!;

    private bool IsDesignMode { get; set; } = false;

    protected override async Task OnInitializedAsync()
    {
        IsDesignMode = ViewState.Type == ViewStateType.PagePreview;
        await Task.CompletedTask;
    }
}
