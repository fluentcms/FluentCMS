namespace FluentCMS.Web.UI;
public partial class PageEditorSidebar
{
    [Parameter]
    public List<PluginDefinitionDetailResponse> PluginDefinitions { get; set; } = [];
}
