namespace FluentCMS.Web.UI;

public partial class PluginContainerActions
{
    [Parameter]
    public PluginViewState Plugin { get; set; } = default!;

    [CascadingParameter]
    public ViewState ViewState { get; set; } = default!;

    private bool IsDesignMode = false;
    private bool HasEditMode = false;

    protected override async Task OnInitializedAsync()
    {
        IsDesignMode = ViewState.Type == ViewStateType.PagePreview;
        HasEditMode = ViewState.Type != ViewStateType.PluginEdit && Plugin?.Definition?.Types.Where(x => x.Name.StartsWith("Edit")).FirstOrDefault() != null;
    }

    private string GetEditUrl()
    {
        var editType = Plugin.Definition.Types.Where(x => x.Name.StartsWith("Edit")).FirstOrDefault();
        return $"?pluginId={Plugin.Id}&viewName={editType.Name}";
    } 
}
