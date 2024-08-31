namespace FluentCMS.Web.UI;

public partial class PluginContainerActions
{
    [Parameter]
    public PluginViewState Plugin { get; set; } = default!;

    [Parameter]
    public string Title { get; set; } = default!;

    [CascadingParameter]
    public ViewState ViewState { get; set; } = default!;

    private bool IsDesignMode = false;

    protected override async Task OnInitializedAsync()
    {
        IsDesignMode = ViewState.Type == ViewStateType.PagePreview;
    }
}
