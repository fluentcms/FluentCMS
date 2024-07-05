namespace FluentCMS.Web.UI;

public partial class PluginContainerActions
{
    [Parameter]
    public PluginDetailResponse Plugin { get; set; } = default!;

    [CascadingParameter]
    public ViewContext ViewContext { get; set; } = default!;

    private bool IsDesignMode = false;
    private bool HasEditMode = false;

    protected override async Task OnInitializedAsync()
    {
        IsDesignMode = ViewContext.Type == ViewType.PagePreview;
        HasEditMode = Plugin?.Definition?.Types.Where(x => x.Name.StartsWith("Edit")).FirstOrDefault() != null;
    }

    private string GetEditUrl()
    {
        var editType = Plugin.Definition.Types.Where(x => x.Name.StartsWith("Edit")).FirstOrDefault();
        return $"?pluginId={Plugin.Id}&viewName={editType.Name}";
    } 
}
