namespace FluentCMS.Web.UI;

public partial class PluginContainerActions : IDisposable
{
    [Parameter]
    public PluginViewState Plugin { get; set; } = default!;

    [Inject]
    public ViewState ViewState { get; set; } = default!;

    private bool IsDesignMode = false;

    protected override async Task OnInitializedAsync()
    {
        IsDesignMode = ViewState.Type == ViewStateType.PagePreview;
        ViewState.OnStateChanged += ViewStateChanged;
    }

    private void ViewStateChanged(object? sender, EventArgs e)
    {
        StateHasChanged();
    }
    void IDisposable.Dispose()
    {
        ViewState.OnStateChanged -= ViewStateChanged;
    }

}
